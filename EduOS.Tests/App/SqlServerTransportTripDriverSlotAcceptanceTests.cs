using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server acceptance for the same driver competing across different
/// vehicles. Independent DbContexts are required; a planning preview cannot
/// establish that a physical driver has only one dispatch slot.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Concurrent_vehicles_sharing_one_driver_cannot_both_dispatch_same_direction()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var first = await PlannedTripAsync(connection);
        var second = await PlanSecondVehicleForSameDriverAsync(first);
        var attempts = new[]
        {
            (Reference: first.Reference, Version: first.RowVersion),
            (Reference: second.Reference, Version: second.RowVersion)
        };

        async Task<(bool Success, int Status)> DispatchAsync(int index)
        {
            await using var db = ScopedContext(first.Fixture.Options,
                first.Fixture.TenantId, first.Fixture.UserId);
            var response = await TripService(db, first.Fixture).DispatchAsync(
                attempts[index].Reference,
                new DispatchTransportTripRequestDto
                {
                    ClientRequestId = Guid.NewGuid(),
                    RowVersion = attempts[index].Version
                });
            return (response.Success, response.StatusCode);
        }

        var responses = await Task.WhenAll(DispatchAsync(0), DispatchAsync(1));
        Assert.True(responses.Count(x => x.Success) <= 1,
            "Two vehicles must never freeze two trips for one driver/date/direction.");
        Assert.All(responses.Where(x => !x.Success), x => Assert.Equal(409, x.Status));

        if (!responses.Any(x => x.Success))
        {
            var retry = await DispatchAsync(0);
            Assert.True(retry.Success, "A fresh request must progress after competing SQL conflicts: " + retry.Status);
        }

        await using var verify = ScopedContext(first.Fixture.Options,
            first.Fixture.TenantId, first.Fixture.UserId);
        var tripRows = await verify.TransportTrips.AsNoTracking()
            .Where(x => x.TenantId == first.Fixture.TenantId &&
                (x.PublicId == first.Reference || x.PublicId == second.Reference))
            .ToListAsync();
        Assert.Equal(2, tripRows.Count);
        var winner = Assert.Single(tripRows.Where(x => x.State == "Dispatched"));
        var loser = Assert.Single(tripRows.Where(x => x.State == "Planned"));
        Assert.NotNull(winner.TransportDriverId);
        Assert.Equal(1, winner.RiderCount);
        Assert.Null(loser.TransportDriverId);
        Assert.Equal(0, loser.RiderCount);
        Assert.Equal(1, await verify.TransportTripRiders.AsNoTracking().CountAsync(x =>
            x.TenantId == first.Fixture.TenantId && x.TransportTripId == winner.Id));
        Assert.Equal(0, await verify.TransportTripRiders.AsNoTracking().CountAsync(x =>
            x.TenantId == first.Fixture.TenantId && x.TransportTripId == loser.Id));

        var denied = await DispatchAsync(loser.PublicId == first.Reference ? 0 : 1);
        Assert.False(denied.Success);
        Assert.Equal(409, denied.Status);
    }

    [Fact]
    public async Task Completed_driver_slot_remains_reserved_against_a_second_vehicle_dispatch()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var first = await PlannedTripAsync(connection);
        var second = await PlanSecondVehicleForSameDriverAsync(first);
        await using (var db = ScopedContext(first.Fixture.Options,
            first.Fixture.TenantId, first.Fixture.UserId))
        {
            var service = TripService(db, first.Fixture);
            var dispatched = await service.DispatchAsync(first.Reference,
                new DispatchTransportTripRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = first.RowVersion
                });
            Assert.True(dispatched.Success, dispatched.Message);
            var attendance = await service.RecordAttendanceAsync(first.Reference,
                new RecordTransportTripAttendanceRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), StudentCode = first.StudentCode,
                    EventType = "NoShow"
                });
            Assert.True(attendance.Success, attendance.Message);
            var version = await db.TransportTrips.AsNoTracking()
                .Where(x => x.TenantId == first.Fixture.TenantId && x.PublicId == first.Reference)
                .Select(x => x.RowVersion).SingleAsync();
            var completed = await service.CompleteAsync(first.Reference,
                new CompleteTransportTripRequestDto
                {
                    ClientRequestId = Guid.NewGuid(),
                    RowVersion = Convert.ToBase64String(version)
                });
            Assert.True(completed.Success, completed.Message);
        }

        await using var contenderDb = ScopedContext(first.Fixture.Options,
            first.Fixture.TenantId, first.Fixture.UserId);
        var denied = await TripService(contenderDb, first.Fixture).DispatchAsync(
            second.Reference, new DispatchTransportTripRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = second.RowVersion
            });
        Assert.False(denied.Success);
        Assert.Equal(409, denied.StatusCode);
        await AssertPlannedAndEmptyAsync(second);
    }

    private static async Task<DispatchedTripFixture> PlanSecondVehicleForSameDriverAsync(
        DispatchedTripFixture first)
    {
        var tenant = first.Fixture.TenantId;
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        await using var db = ScopedContext(first.Fixture.Options, tenant, first.Fixture.UserId);
        var originalVehicle = await db.Vehicles.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.Id == first.Fixture.VehicleId);
        var route = await db.Routes.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.PublicId == first.Fixture.RouteReference);
        var originalLink = await db.RouteVehicleAssignments.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.RouteId == route.Id &&
                x.VehicleId == originalVehicle.Id && x.IsCurrent && !x.IsDeleted);
        var driverId = await db.VehicleDriverAssignments.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.VehicleId == originalVehicle.Id &&
                x.IsCurrent && !x.IsDeleted)
            .Select(x => x.TransportDriverId).SingleAsync();
        var existingRider = await db.StudentTransports.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.VehicleId == originalVehicle.Id &&
                x.State == TransportAssignmentState.Active);
        var secondEnrollment = await db.StudentEnrollments.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.PublicId == first.Fixture.Enrollments[1]);

        var vehicle = new Vehicle
        {
            TenantId = tenant, CampusId = originalVehicle.CampusId,
            VehicleNumber = "TRIP-ALT-" + Guid.NewGuid().ToString("N")[..12],
            Capacity = 1, IsActive = true
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        var routeLink = new RouteVehicleAssignment
        {
            TenantId = tenant, RouteId = route.Id, VehicleId = vehicle.Id,
            EffectiveFrom = today, EffectiveTo = today.AddDays(1), IsCurrent = true
        };
        db.RouteVehicleAssignments.Add(routeLink);
        db.VehicleDriverAssignments.Add(new VehicleDriverAssignment
        {
            TenantId = tenant, VehicleId = vehicle.Id, TransportDriverId = driverId,
            EffectiveFrom = today, EffectiveTo = today.AddDays(1), IsCurrent = true
        });
        await db.SaveChangesAsync();

        db.StudentTransports.Add(new StudentTransport
        {
            TenantId = tenant, ClientRequestId = Guid.NewGuid(),
            StudentId = secondEnrollment.StudentId, StudentEnrollmentId = secondEnrollment.Id,
            RouteVehicleAssignmentId = routeLink.Id, RouteId = originalLink.RouteId,
            VehicleId = vehicle.Id, PickupStopId = existingRider.PickupStopId,
            DropStopId = existingRider.DropStopId, StartDate = today,
            EndDate = today.AddDays(1), State = TransportAssignmentState.Active,
            MonthlyFare = 0m
        });
        await db.SaveChangesAsync();

        var planned = await TripService(db, first.Fixture).PlanAsync(
            new PlanTransportTripRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RouteReference = first.Fixture.RouteReference,
                VehicleReference = vehicle.PublicId, OperatingDate = today, Direction = "Pickup"
            });
        Assert.True(planned.Success, planned.Message);
        return first with { Reference = planned.Data!.Reference,
            RowVersion = planned.Data.RowVersion, DispatchKey = Guid.Empty };
    }
}
