using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Finance;
using EduOS.Service.Services.Platform;
using EduOS.Service.Services.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Claims;
using Xunit;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Tests.App;

/// <summary>
/// Operations-owned SQL Server tests of the real rider command, not just an index
/// or a pure interval helper. The service's serializable capacity check must
/// survive two independent DbContexts racing for the last physical seat.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerTransportSeatCapacityIntegrationTests
{
    private static readonly ConcurrentQueue<string> Diagnostics = new();
    [Fact]
    public async Task Two_concurrent_future_riders_cannot_reserve_the_same_last_seat()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var first = Request(fixture, 0, fixture.Start, fixture.Start.AddDays(6));
        var second = Request(fixture, 1, fixture.Start, fixture.Start.AddDays(6));

        // Separate contexts and connections: EF InMemory cannot establish this guarantee.
        var responses = await Task.WhenAll(AssignAsync(fixture, first), AssignAsync(fixture, second));
        Assert.True(responses.Any(x => x.Success), "No reservation succeeded. " + string.Join("\n", Diagnostics));
        var accepted = Assert.Single(responses, x => x.Success);
        Assert.True(responses.Count(x => !x.Success && x.StatusCode == 409) == 1,
            "Expected a conflict response. Actual: " + string.Join("; ", responses.Select(x => x.StatusCode + ": " + x.Message)) +
            "\n" + string.Join("\n", Diagnostics));

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var active = await verify.StudentTransports.AsNoTracking().Where(x =>
                x.TenantId == fixture.TenantId && x.VehicleId == fixture.VehicleId &&
                !x.IsDeleted && x.State == TransportAssignmentState.Active).ToListAsync();
            Assert.Single(active);
            Assert.Equal(accepted.Data!.Reference, active[0].PublicId);
        }

        // The winner's durable client request ID replays the same allocation,
        // without consuming another seat or rewriting the original reservation.
        var winningRequest = responses[0].Success ? first : second;
        var replay = await AssignAsync(fixture, winningRequest);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(accepted.Data!.Reference, replay.Data!.Reference);
        await using var final = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await final.StudentTransports.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.VehicleId == fixture.VehicleId &&
            !x.IsDeleted && x.State == TransportAssignmentState.Active));
    }

    [Fact]
    public async Task Disjoint_future_periods_can_share_one_seat_without_false_overcapacity()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var first = await AssignAsync(fixture, Request(fixture, 0,
            fixture.Start, fixture.Start.AddDays(4)));
        Assert.True(first.Success, first.Message + "\n" + string.Join("\n", Diagnostics));
        var second = await AssignAsync(fixture, Request(fixture, 1,
            fixture.Start.AddDays(5), fixture.Start.AddDays(9)));
        Assert.True(second.Success, second.Message + "\n" + string.Join("\n", Diagnostics));

        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(2, await verify.StudentTransports.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.VehicleId == fixture.VehicleId &&
            !x.IsDeleted && x.State == TransportAssignmentState.Active));
    }

    [Fact]
    public async Task Capacity_reduction_racing_a_new_rider_never_overbooks_the_vehicle()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);

        // Begin with two seats and one future rider. The competing operations
        // must serialize: either the second rider wins or capacity becomes one.
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var vehicle = await setup.Vehicles.SingleAsync(x => x.Id == fixture.VehicleId);
            vehicle.Capacity = 2;
            await setup.SaveChangesAsync();
        }
        var first = await AssignAsync(fixture, Request(fixture, 0, fixture.Start, fixture.Start.AddDays(6)));
        Assert.True(first.Success, first.Message);

        string rowVersion;
        await using (var snapshot = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var vehicle = await snapshot.Vehicles.AsNoTracking().SingleAsync(x => x.Id == fixture.VehicleId);
            rowVersion = Convert.ToBase64String(vehicle.RowVersion);
        }

        var secondRequest = Request(fixture, 1, fixture.Start, fixture.Start.AddDays(6));
        var bookingTask = AssignAsync(fixture, secondRequest);
        var reductionTask = UpdateCapacityAsync(fixture, new UpdateTransportVehicleRequestDto
        {
            Capacity = 1, IsActive = true, RowVersion = rowVersion
        });
        await Task.WhenAll(bookingTask, reductionTask);
        var booking = await bookingTask;
        var reduction = await reductionTask;

        Assert.True(booking.Success || booking.StatusCode == 409,
            "Rider booking should succeed or return a concurrency/capacity conflict: " + booking.Message);
        Assert.True(reduction.Success || reduction.StatusCode == 409,
            "Capacity reduction should succeed or return a concurrency/capacity conflict: " + reduction.Message);
        Assert.False(booking.Success && reduction.Success,
            "A second overlapping rider and a one-seat capacity reduction cannot both commit.");

        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var persistedVehicle = await verify.Vehicles.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.VehicleId);
        var reservations = await verify.StudentTransports.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.VehicleId == fixture.VehicleId &&
                !x.IsDeleted && x.State == TransportAssignmentState.Active &&
                x.StartDate <= fixture.Start.AddDays(6) &&
                (!x.EndDate.HasValue || x.EndDate.Value >= fixture.Start))
            .ToListAsync();
        Assert.Equal(reduction.Success ? 1 : 2, persistedVehicle.Capacity);
        Assert.Equal(booking.Success ? 2 : 1, reservations.Count);
        Assert.True(reservations.Count <= persistedVehicle.Capacity,
            "Committed reservations exceeded the final physical seat capacity.");
    }

    [Fact]
    public async Task Capacity_reduction_rejects_two_overlapping_future_riders()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var vehicle = await setup.Vehicles.SingleAsync(x => x.Id == fixture.VehicleId);
            vehicle.Capacity = 2;
            await setup.SaveChangesAsync();
        }

        var first = await AssignAsync(fixture, Request(fixture, 0, fixture.Start, fixture.Start.AddDays(6)));
        var second = await AssignAsync(fixture, Request(fixture, 1, fixture.Start, fixture.Start.AddDays(6)));
        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);

        await using var snapshot = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var vehicleBefore = await snapshot.Vehicles.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.VehicleId);
        var reduced = await UpdateCapacityAsync(fixture, new UpdateTransportVehicleRequestDto
        {
            Capacity = 1, IsActive = true,
            RowVersion = Convert.ToBase64String(vehicleBefore.RowVersion)
        });
        Assert.False(reduced.Success);
        Assert.Equal(409, reduced.StatusCode);

        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var vehicleAfter = await verify.Vehicles.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.VehicleId);
        Assert.Equal(2, vehicleAfter.Capacity);
    }


    [Fact]
    public async Task Route_vehicle_create_request_key_replays_original_result_after_rowversion_change()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        CreateTransportRouteVehicleLinkRequestDto request;
        long replacementVehicleId;

        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var route = await setup.Set<TransportRoute>().AsNoTracking()
                .SingleAsync(x => x.PublicId == fixture.RouteReference);
            var vehicle = new Vehicle
            {
                TenantId = fixture.TenantId, CampusId = route.CampusId,
                VehicleNumber = "CAP-REPLAY-" + Guid.NewGuid().ToString("N")[..12],
                Capacity = 2, IsActive = true
            };
            setup.Vehicles.Add(vehicle);
            await setup.SaveChangesAsync();
            replacementVehicleId = vehicle.Id;
            request = new CreateTransportRouteVehicleLinkRequestDto
            {
                ClientRequestId = Guid.NewGuid(),
                RouteId = route.Id, VehicleId = vehicle.Id,
                RouteRowVersion = Convert.ToBase64String(route.RowVersion),
                VehicleRowVersion = Convert.ToBase64String(vehicle.RowVersion),
                EffectiveFrom = fixture.Start.AddDays(1),
                EffectiveTo = fixture.Start.AddDays(6)
            };
        }

        async Task<ApiResponse<RouteVehicleAssignmentDto>> ExecuteAsync(
            CreateTransportRouteVehicleLinkRequestDto command)
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await Service(db, fixture.TenantId, fixture.UserId).AssignRouteVehicleAsync(command);
        }

        var created = await ExecuteAsync(request);
        Assert.True(created.Success, created.Message);
        Assert.Equal(201, created.StatusCode);
        var createdId = created.Data!.Id;

        // Replay must not consume another assignment or require the stale version.
        await using (var edit = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var vehicle = await edit.Vehicles.SingleAsync(x => x.Id == replacementVehicleId);
            vehicle.VehicleType = "Updated after reservation";
            await edit.SaveChangesAsync();
        }

        var replay = await ExecuteAsync(request);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(created.StatusCode, replay.StatusCode);
        Assert.Equal(createdId, replay.Data!.Id);
        Assert.Equal(created.Data.RowVersion, replay.Data.RowVersion);

        var mismatched = new CreateTransportRouteVehicleLinkRequestDto
        {
            ClientRequestId = request.ClientRequestId,
            RouteId = request.RouteId, VehicleId = request.VehicleId,
            RouteRowVersion = request.RouteRowVersion,
            VehicleRowVersion = request.VehicleRowVersion,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo!.Value.AddDays(1)
        };
        var conflict = await ExecuteAsync(mismatched);
        Assert.False(conflict.Success);
        Assert.Equal(409, conflict.StatusCode);

        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await verify.RouteVehicleAssignments.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.VehicleId == replacementVehicleId &&
            !x.IsDeleted && x.IsCurrent));
    }

    [Fact]
    public async Task Parallel_route_vehicle_reservations_cannot_commit_overlapping_windows()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        CreateTransportRouteVehicleLinkRequestDto first, second;
        long vehicleId;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var route = await setup.Set<TransportRoute>().AsNoTracking()
                .SingleAsync(x => x.PublicId == fixture.RouteReference);
            var vehicle = new Vehicle
            {
                TenantId = fixture.TenantId, CampusId = route.CampusId,
                VehicleNumber = "CAP-RACE-" + Guid.NewGuid().ToString("N")[..12],
                Capacity = 2, IsActive = true
            };
            setup.Vehicles.Add(vehicle);
            await setup.SaveChangesAsync();
            vehicleId = vehicle.Id;
            first = new CreateTransportRouteVehicleLinkRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RouteId = route.Id, VehicleId = vehicleId,
                RouteRowVersion = Convert.ToBase64String(route.RowVersion),
                VehicleRowVersion = Convert.ToBase64String(vehicle.RowVersion),
                EffectiveFrom = fixture.Start.AddDays(1), EffectiveTo = fixture.Start.AddDays(6)
            };
            second = new CreateTransportRouteVehicleLinkRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RouteId = route.Id, VehicleId = vehicleId,
                RouteRowVersion = first.RouteRowVersion, VehicleRowVersion = first.VehicleRowVersion,
                EffectiveFrom = fixture.Start.AddDays(2), EffectiveTo = fixture.Start.AddDays(7)
            };
        }
        async Task<ApiResponse<RouteVehicleAssignmentDto>> ExecuteAsync(CreateTransportRouteVehicleLinkRequestDto request)
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await Service(db, fixture.TenantId, fixture.UserId).AssignRouteVehicleAsync(request);
        }
        // Independent DbContexts/SQL connections exercise SERIALIZABLE range locks
        // and the filtered current-vehicle uniqueness constraint.
        var results = await Task.WhenAll(ExecuteAsync(first), ExecuteAsync(second));
        Assert.Single(results, x => x.Success);
        Assert.Single(results, x => !x.Success && x.StatusCode == 409);
        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var committed = await verify.RouteVehicleAssignments.AsNoTracking()
                .Where(x => x.TenantId == fixture.TenantId && x.VehicleId == vehicleId &&
                    !x.IsDeleted && x.IsCurrent).ToListAsync();
            Assert.Equal(results.Single(x => x.Success).Data!.Id, Assert.Single(committed).Id);
        }
        // A successful command's durable receipt must replay without another row.
        var winningRequest = results[0].Success ? first : second;
        var replay = await ExecuteAsync(winningRequest);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(results.Single(x => x.Success).Data!.Id, replay.Data!.Id);
        await using var final = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await final.RouteVehicleAssignments.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.VehicleId == vehicleId &&
            !x.IsDeleted && x.IsCurrent));
    }

    [Fact]
    public async Task Upcoming_route_vehicle_replacement_racing_rider_booking_preserves_route_history()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        long linkId, originalVehicleId, replacementVehicleId;
        ChangeUpcomingRouteVehicleRequestDto change;
        AssignStudentTransportRequestDto booking;

        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var route = await setup.Set<TransportRoute>().AsNoTracking()
                .SingleAsync(x => x.PublicId == fixture.RouteReference);
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var original = new Vehicle
            {
                TenantId = fixture.TenantId, CampusId = route.CampusId,
                VehicleNumber = "CAP-SWAP-OLD-" + suffix, Capacity = 1, IsActive = true
            };
            var replacement = new Vehicle
            {
                TenantId = fixture.TenantId, CampusId = route.CampusId,
                VehicleNumber = "CAP-SWAP-NEW-" + suffix, Capacity = 1, IsActive = true
            };
            setup.Vehicles.AddRange(original, replacement);
            await setup.SaveChangesAsync();
            originalVehicleId = original.Id;
            replacementVehicleId = replacement.Id;
            var link = new RouteVehicleAssignment
            {
                TenantId = fixture.TenantId, RouteId = route.Id, VehicleId = original.Id,
                EffectiveFrom = fixture.Start.AddDays(1),
                EffectiveTo = fixture.Start.AddDays(6), IsCurrent = true
            };
            setup.RouteVehicleAssignments.Add(link);
            await setup.SaveChangesAsync();
            linkId = link.Id;
            change = new ChangeUpcomingRouteVehicleRequestDto
            {
                ClientRequestId = Guid.NewGuid(), NewVehicleId = replacement.Id,
                AssignmentRowVersion = Convert.ToBase64String(link.RowVersion),
                VehicleRowVersion = Convert.ToBase64String(replacement.RowVersion)
            };
            booking = new AssignStudentTransportRequestDto
            {
                ClientRequestId = Guid.NewGuid(),
                StudentEnrollmentReference = fixture.Enrollments[0],
                RouteReference = fixture.RouteReference, VehicleReference = original.PublicId,
                StartDate = fixture.Start.AddDays(2), EndDate = fixture.Start.AddDays(5),
                MonthlyFare = 0m
            };
        }

        async Task<ApiResponse<RouteVehicleAssignmentDto>> ChangeAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await Service(db, fixture.TenantId, fixture.UserId)
                .ChangeUpcomingRouteVehicleAsync(linkId, change);
        }
        async Task<ApiResponse<StudentTransportDto>> BookAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await Service(db, fixture.TenantId, fixture.UserId).AssignAsync(booking);
        }

        // Separate SQL connections must serialize the route-link rewrite against
        // the rider's route-coverage check and canonical reservation insertion.
        var changeTask = ChangeAsync();
        var bookingTask = BookAsync();
        await Task.WhenAll(changeTask, bookingTask);
        var changed = await changeTask;
        var booked = await bookingTask;
        Assert.True(changed.Success || booked.Success,
            "Both competing operations failed: change=" + changed.Message + "; booking=" + booked.Message);
        Assert.False(changed.Success && booked.Success,
            "A rider cannot commit against a route link whose vehicle was simultaneously replaced.");
        Assert.True(changed.Success || changed.StatusCode == 409, changed.Message);
        Assert.True(booked.Success || booked.StatusCode == 409, booked.Message);

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var persisted = await verify.RouteVehicleAssignments.AsNoTracking()
                .SingleAsync(x => x.TenantId == fixture.TenantId && x.Id == linkId);
            Assert.Equal(changed.Success ? replacementVehicleId : originalVehicleId, persisted.VehicleId);
            var riders = await verify.StudentTransports.AsNoTracking().Where(x =>
                x.TenantId == fixture.TenantId && !x.IsDeleted &&
                x.State == TransportAssignmentState.Active &&
                x.RouteVehicleAssignmentId == linkId).ToListAsync();
            Assert.Equal(booked.Success ? 1 : 0, riders.Count);
            if (booked.Success)
                Assert.Equal(originalVehicleId, Assert.Single(riders).VehicleId);
        }

        // Whichever command won must retain its durable request-key outcome.
        if (changed.Success)
        {
            var replay = await ChangeAsync();
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(changed.Data!.Id, replay.Data!.Id);
            Assert.Equal(changed.Data.RowVersion, replay.Data.RowVersion);
        }
        else
        {
            var replay = await BookAsync();
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(booked.Data!.Reference, replay.Data!.Reference);
        }
    }


    [Fact]
    public async Task Competing_upcoming_driver_changes_cannot_double_book_a_replacement_driver()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        long firstLinkId, secondLinkId, replacementDriverId;
        ChangeUpcomingVehicleDriverRequestDto first, second;

        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var originalVehicle = await setup.Vehicles.AsNoTracking()
                .SingleAsync(x => x.Id == fixture.VehicleId);
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var secondVehicle = new Vehicle
            {
                TenantId = fixture.TenantId, CampusId = originalVehicle.CampusId,
                VehicleNumber = "CAP-DRIVER-RACE-" + suffix, Capacity = 2, IsActive = true
            };
            var originalDriver1 = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Driver source one",
                LicenseNumber = "DR-RACE-A-" + suffix,
                LicenseExpiresOn = fixture.Start.AddDays(30), IsActive = true
            };
            var originalDriver2 = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Driver source two",
                LicenseNumber = "DR-RACE-B-" + suffix,
                LicenseExpiresOn = fixture.Start.AddDays(30), IsActive = true
            };
            var replacement = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Driver shared replacement",
                LicenseNumber = "DR-RACE-C-" + suffix,
                LicenseExpiresOn = fixture.Start.AddDays(30), IsActive = true
            };
            setup.AddRange(secondVehicle, originalDriver1, originalDriver2, replacement);
            await setup.SaveChangesAsync();
            replacementDriverId = replacement.Id;
            var firstLink = new VehicleDriverAssignment
            {
                TenantId = fixture.TenantId, VehicleId = fixture.VehicleId,
                TransportDriverId = originalDriver1.Id, EffectiveFrom = fixture.Start.AddDays(1),
                EffectiveTo = fixture.Start.AddDays(8), IsCurrent = true
            };
            var secondLink = new VehicleDriverAssignment
            {
                TenantId = fixture.TenantId, VehicleId = secondVehicle.Id,
                TransportDriverId = originalDriver2.Id, EffectiveFrom = fixture.Start.AddDays(1),
                EffectiveTo = fixture.Start.AddDays(8), IsCurrent = true
            };
            setup.AddRange(firstLink, secondLink);
            await setup.SaveChangesAsync();
            firstLinkId = firstLink.Id;
            secondLinkId = secondLink.Id;
            first = new ChangeUpcomingVehicleDriverRequestDto
            {
                ClientRequestId = Guid.NewGuid(), NewDriverId = replacement.Id,
                AssignmentRowVersion = Convert.ToBase64String(firstLink.RowVersion),
                DriverRowVersion = Convert.ToBase64String(replacement.RowVersion)
            };
            second = new ChangeUpcomingVehicleDriverRequestDto
            {
                ClientRequestId = Guid.NewGuid(), NewDriverId = replacement.Id,
                AssignmentRowVersion = Convert.ToBase64String(secondLink.RowVersion),
                DriverRowVersion = Convert.ToBase64String(replacement.RowVersion)
            };
        }

        async Task<ApiResponse<VehicleDriverAssignmentDto>> ChangeAsync(
            long assignmentId, ChangeUpcomingVehicleDriverRequestDto request)
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await DriverService(db, fixture.TenantId, fixture.UserId)
                .ChangeUpcomingDriverAsync(assignmentId, request);
        }

        // Distinct SQL connections compete for one canonical current driver assignment.
        var responses = await Task.WhenAll(
            ChangeAsync(firstLinkId, first), ChangeAsync(secondLinkId, second));
        Assert.Equal(1, responses.Count(x => x.Success));
        Assert.Equal(1, responses.Count(x => !x.Success && x.StatusCode == 409));

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var links = await verify.Set<VehicleDriverAssignment>().AsNoTracking()
                .Where(x => x.TenantId == fixture.TenantId && !x.IsDeleted &&
                    x.IsCurrent && x.TransportDriverId == replacementDriverId).ToListAsync();
            var persisted = Assert.Single(links);
            Assert.Equal(responses[0].Success ? firstLinkId : secondLinkId, persisted.Id);
            Assert.Equal(2, await verify.Set<VehicleDriverAssignment>().CountAsync(x =>
                x.TenantId == fixture.TenantId && !x.IsDeleted && x.IsCurrent &&
                (x.Id == firstLinkId || x.Id == secondLinkId)));
        }

        var winnerId = responses[0].Success ? firstLinkId : secondLinkId;
        var winnerRequest = responses[0].Success ? first : second;
        var winner = responses[0].Success ? responses[0] : responses[1];
        var replay = await ChangeAsync(winnerId, winnerRequest);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(winner.Data!.Id, replay.Data!.Id);
        Assert.Equal(winner.Data.RowVersion, replay.Data.RowVersion);
    }

    [Fact]
    public async Task Competing_active_driver_replacements_preserve_predecessor_and_one_successor()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var yesterday = today.AddDays(-1);
        long sourceId, originalDriverId, firstDriverId, secondDriverId;
        ReplaceActiveVehicleDriverRequestDto first, second;

        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var original = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Operating source driver",
                LicenseNumber = "ACTIVE-ORIG-" + suffix,
                LicenseExpiresOn = today.AddDays(60), IsActive = true
            };
            var targetA = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Operating replacement A",
                LicenseNumber = "ACTIVE-NEW-A-" + suffix,
                LicenseExpiresOn = today.AddDays(60), IsActive = true
            };
            var targetB = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Operating replacement B",
                LicenseNumber = "ACTIVE-NEW-B-" + suffix,
                LicenseExpiresOn = today.AddDays(60), IsActive = true
            };
            setup.AddRange(original, targetA, targetB);
            await setup.SaveChangesAsync();
            originalDriverId = original.Id;
            firstDriverId = targetA.Id;
            secondDriverId = targetB.Id;
            var source = new VehicleDriverAssignment
            {
                TenantId = fixture.TenantId, VehicleId = fixture.VehicleId,
                TransportDriverId = original.Id, EffectiveFrom = today.AddDays(-7),
                EffectiveTo = today.AddDays(10), IsCurrent = true
            };
            setup.Add(source);
            await setup.SaveChangesAsync();
            sourceId = source.Id;
            var version = Convert.ToBase64String(source.RowVersion);
            first = new ReplaceActiveVehicleDriverRequestDto
            {
                ClientRequestId = Guid.NewGuid(), NewDriverId = targetA.Id,
                AssignmentRowVersion = version,
                DriverRowVersion = Convert.ToBase64String(targetA.RowVersion)
            };
            second = new ReplaceActiveVehicleDriverRequestDto
            {
                ClientRequestId = Guid.NewGuid(), NewDriverId = targetB.Id,
                AssignmentRowVersion = version,
                DriverRowVersion = Convert.ToBase64String(targetB.RowVersion)
            };
        }

        async Task<ApiResponse<VehicleDriverAssignmentDto>> ReplaceAsync(
            ReplaceActiveVehicleDriverRequestDto request)
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await DriverService(db, fixture.TenantId, fixture.UserId)
                .ReplaceActiveDriverAsync(sourceId, request);
        }

        // Two independent SQL Server contexts race on one source rowversion.
        // At most one successor can be committed, even if one transaction deadlocks.
        var responses = await Task.WhenAll(ReplaceAsync(first), ReplaceAsync(second));
        Assert.Single(responses, x => x.Success);
        Assert.Single(responses, x => !x.Success && x.StatusCode == 409);
        var winning = responses.Single(x => x.Success);
        var winningRequest = responses[0].Success ? first : second;
        var winningDriverId = responses[0].Success ? firstDriverId : secondDriverId;

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var history = await verify.Set<VehicleDriverAssignment>().AsNoTracking()
                .Where(x => x.TenantId == fixture.TenantId && !x.IsDeleted &&
                    x.VehicleId == fixture.VehicleId &&
                    (x.Id == sourceId || x.EffectiveFrom == today))
                .OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, history.Count);
            var predecessor = Assert.Single(history, x => x.Id == sourceId);
            Assert.False(predecessor.IsCurrent);
            Assert.Equal(originalDriverId, predecessor.TransportDriverId);
            Assert.Equal(today.AddDays(-7), predecessor.EffectiveFrom);
            Assert.Equal(yesterday, predecessor.EffectiveTo);
            var successor = Assert.Single(history, x => x.Id != sourceId);
            Assert.True(successor.IsCurrent);
            Assert.Equal(today, successor.EffectiveFrom);
            Assert.Equal(winningDriverId, successor.TransportDriverId);
            Assert.Equal(winning.Data!.Id, successor.Id);
            Assert.Equal(1, await verify.Set<VehicleDriverAssignment>().CountAsync(x =>
                x.TenantId == fixture.TenantId && !x.IsDeleted &&
                x.VehicleId == fixture.VehicleId && x.IsCurrent));
        }

        // Durable replay must return the original successor without rewriting
        // the retired source, successor audit or SQL rowversion.
        var replay = await ReplaceAsync(winningRequest);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(winning.Data!.Id, replay.Data!.Id);
        Assert.Equal(winning.Data.RowVersion, replay.Data.RowVersion);

        var reusedKey = new ReplaceActiveVehicleDriverRequestDto
        {
            ClientRequestId = winningRequest.ClientRequestId,
            NewDriverId = winningDriverId == firstDriverId ? secondDriverId : firstDriverId,
            AssignmentRowVersion = winningRequest.AssignmentRowVersion,
            DriverRowVersion = winningRequest.DriverRowVersion
        };
        var conflict = await ReplaceAsync(reusedKey);
        Assert.False(conflict.Success);
        Assert.Equal(409, conflict.StatusCode);

        await using var final = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await final.Set<VehicleDriverAssignment>().CountAsync(x =>
            x.TenantId == fixture.TenantId && !x.IsDeleted &&
            x.VehicleId == fixture.VehicleId && x.IsCurrent));
    }

    [Fact]
    public async Task Active_driver_replacement_racing_a_new_driver_assignment_cannot_double_book_driver()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var yesterday = today.AddDays(-1);
        long sourceId, replacementDriverId, competingVehicleId;
        ReplaceActiveVehicleDriverRequestDto replacementRequest;
        AssignVehicleDriverRequestDto assignmentRequest;

        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var originalVehicle = await setup.Vehicles.AsNoTracking()
                .SingleAsync(x => x.Id == fixture.VehicleId);
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var competingVehicle = new Vehicle
            {
                TenantId = fixture.TenantId, CampusId = originalVehicle.CampusId,
                VehicleNumber = "CAP-ACTIVE-RACE-" + suffix, Capacity = 2, IsActive = true
            };
            var originalDriver = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Operating source driver",
                LicenseNumber = "ACT-RACE-SRC-" + suffix,
                LicenseExpiresOn = today.AddDays(60), IsActive = true
            };
            var replacementDriver = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Shared competing driver",
                LicenseNumber = "ACT-RACE-TGT-" + suffix,
                LicenseExpiresOn = today.AddDays(60), IsActive = true
            };
            setup.AddRange(competingVehicle, originalDriver, replacementDriver);
            await setup.SaveChangesAsync();
            competingVehicleId = competingVehicle.Id;
            replacementDriverId = replacementDriver.Id;
            var source = new VehicleDriverAssignment
            {
                TenantId = fixture.TenantId, VehicleId = fixture.VehicleId,
                TransportDriverId = originalDriver.Id,
                EffectiveFrom = today.AddDays(-7), EffectiveTo = today.AddDays(10),
                IsCurrent = true
            };
            setup.Add(source);
            await setup.SaveChangesAsync();
            sourceId = source.Id;
            replacementRequest = new ReplaceActiveVehicleDriverRequestDto
            {
                ClientRequestId = Guid.NewGuid(), NewDriverId = replacementDriver.Id,
                AssignmentRowVersion = Convert.ToBase64String(source.RowVersion),
                DriverRowVersion = Convert.ToBase64String(replacementDriver.RowVersion)
            };
            assignmentRequest = new AssignVehicleDriverRequestDto
            {
                ClientRequestId = Guid.NewGuid(), VehicleId = competingVehicle.Id,
                TransportDriverId = replacementDriver.Id,
                EffectiveFrom = today, EffectiveTo = today.AddDays(10),
                VehicleRowVersion = Convert.ToBase64String(competingVehicle.RowVersion),
                DriverRowVersion = Convert.ToBase64String(replacementDriver.RowVersion)
            };
        }

        async Task<ApiResponse<VehicleDriverAssignmentDto>> ReplaceAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await DriverService(db, fixture.TenantId, fixture.UserId)
                .ReplaceActiveDriverAsync(sourceId, replacementRequest);
        }

        async Task<ApiResponse<VehicleDriverAssignmentDto>> AssignDriverAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await DriverService(db, fixture.TenantId, fixture.UserId)
                .AssignDriverAsync(assignmentRequest);
        }

        // The two commands use independent SQL connections and compete for the
        // same tenant-scoped current-driver uniqueness constraint.
        var results = await Task.WhenAll(ReplaceAsync(), AssignDriverAsync());
        Assert.Single(results, x => x.Success);
        Assert.Single(results, x => !x.Success && x.StatusCode == 409);
        var replacementWon = results[0].Success;
        var winner = replacementWon ? results[0] : results[1];

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var assigned = await verify.Set<VehicleDriverAssignment>().AsNoTracking()
                .Where(x => x.TenantId == fixture.TenantId && !x.IsDeleted &&
                    x.IsCurrent && x.TransportDriverId == replacementDriverId).ToListAsync();
            var onlyAssignment = Assert.Single(assigned);
            Assert.Equal(replacementWon ? fixture.VehicleId : competingVehicleId,
                onlyAssignment.VehicleId);
            Assert.Equal(winner.Data!.Id, onlyAssignment.Id);

            var predecessor = await verify.Set<VehicleDriverAssignment>().AsNoTracking()
                .SingleAsync(x => x.Id == sourceId);
            Assert.Equal(today.AddDays(-7), predecessor.EffectiveFrom);
            Assert.Equal(replacementWon ? yesterday : today.AddDays(10), predecessor.EffectiveTo);
            Assert.Equal(!replacementWon, predecessor.IsCurrent);
            Assert.Equal(replacementWon ? 1 : 0, await verify.Set<VehicleDriverAssignment>()
                .CountAsync(x => x.TenantId == fixture.TenantId && !x.IsDeleted &&
                    x.VehicleId == fixture.VehicleId && x.TransportDriverId == replacementDriverId &&
                    x.IsCurrent));
        }

        // The winning durable request key must replay without inserting another
        // assignment or rewriting the SQL rowversion.
        var replay = replacementWon ? await ReplaceAsync() : await AssignDriverAsync();
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(winner.Data!.Id, replay.Data!.Id);
        Assert.Equal(winner.Data.RowVersion, replay.Data.RowVersion);

        await using var final = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await final.Set<VehicleDriverAssignment>().CountAsync(x =>
            x.TenantId == fixture.TenantId && !x.IsDeleted &&
            x.TransportDriverId == replacementDriverId && x.IsCurrent));
    }


    [Fact]
    public async Task Driver_license_shortening_racing_future_driver_assignment_cannot_strand_coverage()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long driverId;
        string vehicleVersion;
        string driverVersion;

        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var driver = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "License race driver",
                LicenseNumber = "LIC-RACE-" + suffix,
                LicenseExpiresOn = today.AddDays(60), IsActive = true
            };
            setup.TransportDrivers.Add(driver);
            await setup.SaveChangesAsync();
            driverId = driver.Id;
            driverVersion = Convert.ToBase64String(driver.RowVersion);
            var vehicle = await setup.Vehicles.AsNoTracking()
                .SingleAsync(x => x.Id == fixture.VehicleId);
            vehicleVersion = Convert.ToBase64String(vehicle.RowVersion);
        }

        var expiryChange = new SaveTransportDriverRequestDto
        {
            FullName = "License race driver", LicenseNumber = "LIC-RACE-" + suffix,
            LicenseExpiresOn = today.AddDays(5), IsActive = true,
            RowVersion = driverVersion
        };
        var assignment = new AssignVehicleDriverRequestDto
        {
            ClientRequestId = Guid.NewGuid(), VehicleId = fixture.VehicleId,
            TransportDriverId = driverId, EffectiveFrom = today.AddDays(1),
            EffectiveTo = today.AddDays(20),
            VehicleRowVersion = vehicleVersion, DriverRowVersion = driverVersion
        };

        async Task<ApiResponse<TransportDriverDto>> ShortenLicenseAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await DriverService(db, fixture.TenantId, fixture.UserId)
                .SaveDriverAsync(driverId, expiryChange);
        }

        async Task<ApiResponse<VehicleDriverAssignmentDto>> AssignDriverAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await DriverService(db, fixture.TenantId, fixture.UserId)
                .AssignDriverAsync(assignment);
        }

        var shortenTask = ShortenLicenseAsync();
        var assignTask = AssignDriverAsync();
        await Task.WhenAll(shortenTask, assignTask);
        var shorten = await shortenTask;
        var assign = await assignTask;
        Assert.Single(new[] { shorten.Success, assign.Success }, success => success);
        Assert.Equal(409, shorten.Success ? assign.StatusCode : shorten.StatusCode);

        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var persistedDriver = await verify.TransportDrivers.AsNoTracking()
            .SingleAsync(x => x.Id == driverId);
        var links = await verify.Set<VehicleDriverAssignment>().AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && !x.IsDeleted &&
                x.TransportDriverId == driverId && x.IsCurrent).ToListAsync();
        Assert.Equal(assign.Success ? 1 : 0, links.Count);
        Assert.Equal(shorten.Success ? today.AddDays(5) : today.AddDays(60),
            persistedDriver.LicenseExpiresOn);
        Assert.All(links, link =>
            Assert.True(link.EffectiveTo.HasValue &&
                link.EffectiveTo.Value <= persistedDriver.LicenseExpiresOn!.Value));

        if (assign.Success)
        {
            var replay = await AssignDriverAsync();
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(assign.Data!.Id, replay.Data!.Id);
            Assert.Equal(assign.Data.RowVersion, replay.Data.RowVersion);
        }
    }

    [Fact]
    public async Task Closing_route_vehicle_link_racing_future_rider_booking_cannot_strand_reservation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        long linkId;
        CloseTransportAssignmentRequestDto close;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var link = await setup.RouteVehicleAssignments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.VehicleId == fixture.VehicleId &&
                x.IsCurrent && !x.IsDeleted);
            link.EffectiveTo = null;
            await setup.SaveChangesAsync();
            linkId = link.Id;
            close = new CloseTransportAssignmentRequestDto
            {
                EffectiveTo = fixture.Start.AddDays(1),
                RowVersion = Convert.ToBase64String(link.RowVersion)
            };
        }
        var booking = Request(fixture, 0, fixture.Start.AddDays(2), fixture.Start.AddDays(5));

        async Task<ApiResponse<RouteVehicleAssignmentDto>> CloseAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await Service(db, fixture.TenantId, fixture.UserId)
                .CloseRouteVehicleAssignmentAsync(linkId, close);
        }
        async Task<ApiResponse<StudentTransportDto>> BookAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await Service(db, fixture.TenantId, fixture.UserId).AssignAsync(booking);
        }

        // Separate SQL connections: neither a closed route link nor a stranded
        // future rider is an acceptable result of this race.
        var closeTask = CloseAsync();
        var bookingTask = BookAsync();
        await Task.WhenAll(closeTask, bookingTask);
        var closed = await closeTask;
        var booked = await bookingTask;
        Assert.True(closed.Success || booked.Success,
            "Both operations failed: closure=" + closed.Message + "; booking=" + booked.Message);
        Assert.False(closed.Success && booked.Success,
            "A future rider cannot commit against a concurrently shortened route link.");
        Assert.True(closed.Success || closed.StatusCode == 409, closed.Message);
        Assert.True(booked.Success || booked.StatusCode == 409, booked.Message);

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var persisted = await verify.RouteVehicleAssignments.AsNoTracking()
                .SingleAsync(x => x.Id == linkId && x.TenantId == fixture.TenantId);
            Assert.Equal(closed.Success ? fixture.Start.AddDays(1) : (DateOnly?)null,
                persisted.EffectiveTo);
            var riders = await verify.StudentTransports.AsNoTracking().Where(x =>
                x.TenantId == fixture.TenantId && !x.IsDeleted &&
                x.RouteVehicleAssignmentId == linkId &&
                x.State == TransportAssignmentState.Active).ToListAsync();
            Assert.Equal(booked.Success ? 1 : 0, riders.Count);
            if (booked.Success)
                Assert.True(persisted.EffectiveTo == null ||
                    persisted.EffectiveTo.Value >= Assert.Single(riders).EndDate);
        }
        if (booked.Success)
        {
            var replay = await BookAsync();
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(booked.Data!.Reference, replay.Data!.Reference);
        }
    }

    [Fact]
    public async Task Driver_link_closure_rejects_existing_future_rider_without_changing_history()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        long linkId;
        CloseTransportAssignmentRequestDto close;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
            var driver = new TransportDriver
            {
                TenantId = fixture.TenantId, FullName = "Closure safety driver",
                LicenseNumber = "CLOSE-SAFE-" + Guid.NewGuid().ToString("N")[..12],
                LicenseExpiresOn = today.AddDays(90), IsActive = true
            };
            setup.TransportDrivers.Add(driver);
            await setup.SaveChangesAsync();
            var link = new VehicleDriverAssignment
            {
                TenantId = fixture.TenantId, VehicleId = fixture.VehicleId,
                TransportDriverId = driver.Id, EffectiveFrom = today,
                EffectiveTo = null, IsCurrent = true
            };
            setup.Add(link);
            await setup.SaveChangesAsync();
            linkId = link.Id;
            close = new CloseTransportAssignmentRequestDto
            {
                EffectiveTo = fixture.Start.AddDays(1),
                RowVersion = Convert.ToBase64String(link.RowVersion)
            };
        }

        var booking = await AssignAsync(fixture,
            Request(fixture, 0, fixture.Start, fixture.Start.AddDays(5)));
        Assert.True(booking.Success, booking.Message);
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var result = await DriverService(db, fixture.TenantId, fixture.UserId)
                .CloseDriverAssignmentAsync(linkId, close);
            Assert.False(result.Success);
            Assert.Equal(409, result.StatusCode);
        }
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var persisted = await verify.Set<VehicleDriverAssignment>().AsNoTracking()
            .SingleAsync(x => x.TenantId == fixture.TenantId && x.Id == linkId);
        Assert.Null(persisted.EffectiveTo);
        Assert.True(persisted.IsCurrent);
        Assert.Equal(close.RowVersion, Convert.ToBase64String(persisted.RowVersion));
        Assert.Equal(1, await verify.StudentTransports.CountAsync(x =>
            x.TenantId == fixture.TenantId && !x.IsDeleted &&
            x.State == TransportAssignmentState.Active && x.VehicleId == fixture.VehicleId));
    }

    private static AssignStudentTransportRequestDto Request(
        Fixture fixture, int student, DateOnly from, DateOnly to) => new()
    {
        ClientRequestId = Guid.NewGuid(),
        StudentEnrollmentReference = fixture.Enrollments[student],
        RouteReference = fixture.RouteReference,
        VehicleReference = fixture.VehicleReference,
        StartDate = from,
        EndDate = to,
        MonthlyFare = 0m
    };

    private static async Task<ApiResponse<StudentTransportDto>> AssignAsync(
        Fixture fixture, AssignStudentTransportRequestDto request)
    {
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        return await Service(db, fixture.TenantId, fixture.UserId).AssignAsync(request);
    }

    private static async Task<ApiResponse<TransportVehicleSetupRowDto>> UpdateCapacityAsync(
        Fixture fixture, UpdateTransportVehicleRequestDto request)
    {
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        return await Service(db, fixture.TenantId, fixture.UserId)
            .UpdateVehicleSetupAsync(fixture.VehicleReference, request);
    }


    private static TransportService DriverService(EduOSDbContext db, long tenant, long user) => new(
        new GenericRepository<TransportRoute>(db),
        new GenericRepository<RouteStop>(db),
        new GenericRepository<Vehicle>(db),
        new GenericRepository<RouteVehicleAssignment>(db),
        new GenericRepository<StudentTransport>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db),
        new GenericRepository<UserCampusAccess>(db),
        new GenericRepository<Campus>(db),
        db,
        new TenantAdmin(tenant, user),
        TimeProvider.System,
        new CaptureLogger(),
        new GenericRepository<TransportDriver>(db),
        new GenericRepository<VehicleDriverAssignment>(db),
        new ReservationFinancePolicy(db),
        new ReservationCommandReceiptStore(db, TimeProvider.System));

    private static TransportService Service(EduOSDbContext db, long tenant, long user) => new(
        new GenericRepository<TransportRoute>(db),
        new GenericRepository<RouteStop>(db),
        new GenericRepository<Vehicle>(db),
        new GenericRepository<RouteVehicleAssignment>(db),
        new GenericRepository<StudentTransport>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db),
        new GenericRepository<UserCampusAccess>(db),
        new GenericRepository<Campus>(db),
        db,
        new TenantAdmin(tenant, user),
        TimeProvider.System,
        new CaptureLogger(),
        new ReservationFinancePolicy(db),
        new ReservationCommandReceiptStore(db, TimeProvider.System));

    private static async Task<Fixture> SeedAsync(string connection)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        await using var db = new EduOSDbContext(options);
        var tenant = new Tenant
        {
            Code = "CI-CAP-" + suffix, Name = "Transport seat concurrency CI",
            Email = "cap-" + suffix + "@example.test", State = TenantState.Active
        };
        var user = new ApplicationUser
        {
            UserName = "cap-" + suffix, NormalizedUserName = ("cap-" + suffix).ToUpperInvariant(),
            Email = "cap-" + suffix + "@example.test",
            NormalizedEmail = ("cap-" + suffix + "@example.test").ToUpperInvariant(),
            FullName = "Transport capacity tester", EmailConfirmed = true, IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        var personA = new Person { FullName = "Capacity CI student A" };
        var personB = new Person { FullName = "Capacity CI student B" };
        db.AddRange(tenant, user, personA, personB);
        await db.SaveChangesAsync();

        using (db.BeginSystemTenantScope(tenant.Id))
        {
            var campus = new Campus
            {
                TenantId = tenant.Id, Code = "CAP-" + suffix,
                Name = "Capacity test campus", IsActive = true
            };
            var year = new AcademicYear
            {
                TenantId = tenant.Id, Name = "Capacity year", Code = "CY-" + suffix,
                StartDate = start.AddDays(-60), EndDate = start.AddDays(365),
                IsActive = true
            };
            var program = new AcademicProgram
            {
                TenantId = tenant.Id, Name = "Capacity program", Code = "CP-" + suffix
            };
            var route = new TransportRoute
            {
                TenantId = tenant.Id, Name = "Capacity route", Code = "CR-" + suffix,
                DefaultFare = 0m, IsActive = true
            };
            var vehicle = new Vehicle
            {
                TenantId = tenant.Id, VehicleNumber = "CAP-BUS-" + suffix,
                Capacity = 1, IsActive = true
            };
            var students = new[]
            {
                new Student
                {
                    TenantId = tenant.Id, PersonId = personA.Id,
                    StudentCode = "CAP-A-" + suffix, FullName = "Capacity student A",
                    AdmissionDate = start.AddDays(-30), StatusCode = "Active"
                },
                new Student
                {
                    TenantId = tenant.Id, PersonId = personB.Id,
                    StudentCode = "CAP-B-" + suffix, FullName = "Capacity student B",
                    AdmissionDate = start.AddDays(-30), StatusCode = "Active"
                }
            };
            db.AddRange(campus, year, program, route, vehicle);
            db.Students.AddRange(students);
            await db.SaveChangesAsync();
            route.CampusId = campus.Id;
            vehicle.CampusId = campus.Id;
            var level = new AcademicLevel
            {
                TenantId = tenant.Id, AcademicProgramId = program.Id,
                Name = "Capacity level", Code = "CL-" + suffix, LevelNo = 1
            };
            var curriculum = new AcademicCurriculum
            {
                TenantId = tenant.Id, AcademicProgramId = program.Id,
                Name = "Capacity curriculum", Code = "CC-" + suffix,
                EffectiveFrom = start.AddDays(-90), IsCurrent = true, IsActive = true
            };
            db.AddRange(level, curriculum);
            await db.SaveChangesAsync();
            var batch = new AcademicBatch
            {
                TenantId = tenant.Id, CampusId = campus.Id, AcademicYearId = year.Id,
                AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                Name = "Capacity batch", Code = "CB-" + suffix,
                Capacity = 30, IsActive = true
            };
            db.AcademicBatches.Add(batch);
            await db.SaveChangesAsync();
            var enrollments = students.Select((student, index) => new StudentEnrollment
            {
                TenantId = tenant.Id, ClientRequestId = Guid.NewGuid(),
                StudentId = student.Id, CampusId = campus.Id,
                AcademicYearId = year.Id, AcademicProgramId = program.Id,
                AcademicLevelId = level.Id, AcademicBatchId = batch.Id,
                AcademicCurriculumId = curriculum.Id,
                RollNo = (index + 1).ToString(), EnrollmentDate = start.AddDays(-30),
                IsCurrent = true, State = EnrollmentState.Active
            }).ToArray();
            db.StudentEnrollments.AddRange(enrollments);
            db.RouteVehicleAssignments.Add(new RouteVehicleAssignment
            {
                TenantId = tenant.Id, RouteId = route.Id, VehicleId = vehicle.Id,
                EffectiveFrom = start.AddDays(-1), EffectiveTo = start.AddDays(30),
                IsCurrent = true
            });
            await db.SaveChangesAsync();
            return new Fixture(options, tenant.Id, user.Id, vehicle.Id,
                route.PublicId, vehicle.PublicId,
                enrollments.Select(x => x.PublicId).ToArray(), start);
        }
    }

    private static EduOSDbContext ScopedContext(
        DbContextOptions<EduOSDbContext> options, long tenant, long user)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class CaptureLogger : ILogger<TransportService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception != null) Diagnostics.Enqueue(exception.ToString());
        }
    }

    private sealed record Fixture(DbContextOptions<EduOSDbContext> Options,
        long TenantId, long UserId, long VehicleId,
        Guid RouteReference, Guid VehicleReference,
        Guid[] Enrollments, DateOnly Start);

    private sealed class TenantAdmin(long tenant, long user) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => "Transport capacity tester";
        public string? Email => "capacity@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL integration tests";
    }
}
