using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server acceptance of the persisted Transport V2 trip lifecycle.
/// Each contender uses its own tenant-scoped DbContext; never a shared in-memory provider.
/// Reuses the isolated institution, enrollment and vehicle fixture from the capacity suite.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Trip_dispatch_freezes_one_rider_and_replays_without_duplicate_snapshots()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var service = TripService(db, state.Fixture);
        var persisted = await db.TransportTrips.AsNoTracking().SingleAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.PublicId == state.Reference);
        Assert.Equal("Dispatched", persisted.State);
        Assert.Equal(1, persisted.RiderCount);
        var replay = await service.DispatchAsync(state.Reference, new DispatchTransportTripRequestDto
        {
            ClientRequestId = state.DispatchKey,
            RowVersion = Convert.ToBase64String(persisted.RowVersion)
        });
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(state.Reference, replay.Data!.Reference);
        var riders = await service.GetRidersAsync(state.Reference, 1, 25);
        Assert.True(riders.Success, riders.Message);
        Assert.Equal(state.StudentCode, Assert.Single(riders.Data!.Items).StudentCode);
        Assert.Equal(1, await db.TransportTripRiders.CountAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.TransportTripId == persisted.Id));
        var export = await service.ExportRidersCsvAsync(state.Reference);
        Assert.True(export.Success, export.Message);
        Assert.Contains(state.StudentCode, export.Data);
    }

    [Fact]
    public async Task Competing_boarded_and_no_show_events_commit_exactly_one_first_event()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        async Task<(bool Success, int Status)> RecordAsync(string eventType)
        {
            await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
            var response = await TripService(db, state.Fixture).RecordAttendanceAsync(
                state.Reference, new RecordTransportTripAttendanceRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode,
                    EventType = eventType
                });
            return (response.Success, response.StatusCode);
        }
        var results = await Task.WhenAll(RecordAsync("Boarded"), RecordAsync("NoShow"));
        Assert.Single(results.Where(x => x.Success));
        Assert.Single(results.Where(x => !x.Success && x.Status == 409));
        await using var verify = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var tripId = await verify.TransportTrips.Where(x => x.PublicId == state.Reference)
            .Select(x => x.Id).SingleAsync();
        var riderId = await verify.TransportTripRiders.Where(x => x.TransportTripId == tripId)
            .Select(x => x.Id).SingleAsync();
        var events = await verify.TransportTripAttendanceEvents.AsNoTracking()
            .Where(x => x.TenantId == state.Fixture.TenantId && x.TransportTripRiderId == riderId)
            .ToListAsync();
        Assert.Single(events);
        Assert.Equal(1, events[0].Sequence);
        Assert.Contains(events[0].EventType, new[] { "Boarded", "NoShow" });
    }

    [Fact]
    public async Task Competing_completions_lock_trip_once_and_replay_winner()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        string version;
        await using (var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var service = TripService(db, state.Fixture);
            var recorded = await service.RecordAttendanceAsync(state.Reference,
                new RecordTransportTripAttendanceRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode,
                    EventType = "NoShow"
                });
            Assert.True(recorded.Success, recorded.Message);
            version = Convert.ToBase64String(await db.TransportTrips.AsNoTracking()
                .Where(x => x.PublicId == state.Reference).Select(x => x.RowVersion).SingleAsync());
        }
        var keys = new[] { Guid.NewGuid(), Guid.NewGuid() };
        async Task<(bool Success, int Status)> CompleteAsync(Guid key)
        {
            await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
            var response = await TripService(db, state.Fixture).CompleteAsync(state.Reference,
                new CompleteTransportTripRequestDto { ClientRequestId = key, RowVersion = version });
            return (response.Success, response.StatusCode);
        }
        var results = await Task.WhenAll(CompleteAsync(keys[0]), CompleteAsync(keys[1]));
        Assert.Single(results.Where(x => x.Success));
        Assert.Single(results.Where(x => !x.Success && x.Status == 409));
        var winner = keys[Array.FindIndex(results, x => x.Success)];
        await using var verify = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var trip = await verify.TransportTrips.AsNoTracking().SingleAsync(x => x.PublicId == state.Reference);
        Assert.Equal("Completed", trip.State);
        Assert.Equal(winner, trip.CompletionRequestId);
        var replay = await TripService(verify, state.Fixture).CompleteAsync(state.Reference,
            new CompleteTransportTripRequestDto { ClientRequestId = winner, RowVersion = version });
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(409, (await TripService(verify, state.Fixture).RecordAttendanceAsync(
            state.Reference, new RecordTransportTripAttendanceRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode,
                EventType = "Boarded"
            })).StatusCode);
    }

    [Fact]
    public async Task Competing_corrections_keep_one_immutable_successor_and_replay()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        await using (var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var service = TripService(db, state.Fixture);
            var recorded = await service.RecordAttendanceAsync(state.Reference,
                new RecordTransportTripAttendanceRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode,
                    EventType = "NoShow"
                });
            Assert.True(recorded.Success, recorded.Message);
            var version = Convert.ToBase64String(await db.TransportTrips.AsNoTracking()
                .Where(x => x.PublicId == state.Reference).Select(x => x.RowVersion).SingleAsync());
            var completed = await service.CompleteAsync(state.Reference,
                new CompleteTransportTripRequestDto { ClientRequestId = Guid.NewGuid(), RowVersion = version });
            Assert.True(completed.Success, completed.Message);
        }
        var keys = new[] { Guid.NewGuid(), Guid.NewGuid() };
        async Task<(bool Success, int Status)> CorrectAsync(Guid key)
        {
            await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
            var response = await TripService(db, state.Fixture).CorrectAttendanceAsync(state.Reference,
                new CorrectTransportTripAttendanceRequestDto
                {
                    ClientRequestId = key, StudentCode = state.StudentCode,
                    ExpectedState = "NoShow", CorrectedState = "Dropped",
                    Reason = "Verified guardian pickup correction"
                });
            return (response.Success, response.StatusCode);
        }
        var results = await Task.WhenAll(CorrectAsync(keys[0]), CorrectAsync(keys[1]));
        Assert.Single(results.Where(x => x.Success));
        Assert.Single(results.Where(x => !x.Success && x.Status == 409));
        var winner = keys[Array.FindIndex(results, x => x.Success)];
        await using var verify = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var history = await TripService(verify, state.Fixture).GetAttendanceHistoryAsync(
            state.Reference, state.StudentCode);
        Assert.True(history.Success, history.Message);
        Assert.Equal(2, history.Data!.Count);
        Assert.Equal("NoShow", history.Data[0].EventType);
        Assert.Equal("Dropped", history.Data[1].EventType);
        Assert.True(history.Data[1].IsCorrection);
        var replay = await CorrectAsync(winner);
        Assert.True(replay.Success);
        var tripId = await verify.TransportTrips.Where(x => x.PublicId == state.Reference)
            .Select(x => x.Id).SingleAsync();
        var riderId = await verify.TransportTripRiders.Where(x => x.TransportTripId == tripId)
            .Select(x => x.Id).SingleAsync();
        Assert.Equal(2, await verify.TransportTripAttendanceEvents.CountAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.TransportTripRiderId == riderId));
        var manifest = await TripService(verify, state.Fixture).GetRidersAsync(state.Reference, 1, 25);
        Assert.Equal("Dropped", Assert.Single(manifest.Data!.Items).AttendanceState);
    }

    [Fact]
    public async Task Guessed_trip_rider_and_history_are_not_accessible_across_tenant_campus_or_role()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        await using var otherDb = ScopedContext(state.Fixture.Options, long.MaxValue, state.Fixture.UserId);
        var other = new TransportTripService(otherDb,
            new TenantAdmin(long.MaxValue, state.Fixture.UserId),
            TimeProvider.System, NullLogger<TransportTripService>.Instance);
        Assert.Equal(404, (await other.GetRidersAsync(state.Reference, 1, 25)).StatusCode);
        Assert.Equal(404, (await other.GetAttendanceHistoryAsync(state.Reference, state.StudentCode)).StatusCode);
        Assert.Equal(404, (await other.RecordAttendanceAsync(state.Reference,
            new RecordTransportTripAttendanceRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode, EventType = "Boarded"
            })).StatusCode);
        Assert.Equal(404, (await other.ExportRidersCsvAsync(state.Reference)).StatusCode);
        await using var campusDb = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var campusDenied = new TransportTripService(campusDb,
            new RestrictedTransportManager(state.Fixture.TenantId, state.Fixture.UserId),
            TimeProvider.System, NullLogger<TransportTripService>.Instance);
        Assert.Equal(403, (await campusDenied.GetRidersAsync(state.Reference, 1, 25)).StatusCode);
        Assert.Equal(403, (await campusDenied.RecordAttendanceAsync(state.Reference,
            new RecordTransportTripAttendanceRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode, EventType = "NoShow"
            })).StatusCode);
        Assert.Equal(404, (await TripService(campusDb, state.Fixture).GetAttendanceHistoryAsync(
            Guid.NewGuid(), state.StudentCode)).StatusCode);
        Assert.Equal(404, (await TripService(campusDb, state.Fixture).RecordAttendanceAsync(
            state.Reference, new RecordTransportTripAttendanceRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentCode = "GUESS-NOT-IN-TRIP", EventType = "Boarded"
            })).StatusCode);
    }

    private static TransportTripService TripService(EduOSDbContext db, Fixture fixture) =>
        new(db, new TenantAdmin(fixture.TenantId, fixture.UserId),
            TimeProvider.System, NullLogger<TransportTripService>.Instance);

    private sealed record DispatchedTripFixture(Fixture Fixture, Guid Reference,
        Guid DispatchKey, string StudentCode);

    private static async Task<DispatchedTripFixture> DispatchedTripAsync(string connection)
    {
        var fixture = await SeedAsync(connection);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var route = await db.Routes.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == fixture.RouteReference);
        var enrollment = await db.StudentEnrollments.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
        var pickup = new RouteStop
        {
            TenantId = fixture.TenantId, RouteId = route.Id,
            Name = "Pickup test stop", SequenceNo = 1, IsActive = true
        };
        var drop = new RouteStop
        {
            TenantId = fixture.TenantId, RouteId = route.Id,
            Name = "Drop test stop", SequenceNo = 2, IsActive = true
        };
        var driver = new TransportDriver
        {
            TenantId = fixture.TenantId, FullName = "Trip lifecycle driver",
            LicenseNumber = "CI-TRIP-" + Guid.NewGuid().ToString("N")[..12],
            LicenseExpiresOn = today.AddDays(60), IsActive = true
        };
        db.AddRange(pickup, drop, driver);
        await db.SaveChangesAsync();
        var routeLink = new RouteVehicleAssignment
        {
            TenantId = fixture.TenantId, RouteId = route.Id, VehicleId = fixture.VehicleId,
            EffectiveFrom = today, EffectiveTo = today.AddDays(1), IsCurrent = true
        };
        db.AddRange(routeLink, new VehicleDriverAssignment
        {
            TenantId = fixture.TenantId, VehicleId = fixture.VehicleId,
            TransportDriverId = driver.Id, EffectiveFrom = today,
            EffectiveTo = today.AddDays(1), IsCurrent = true
        });
        await db.SaveChangesAsync();
        db.StudentTransports.Add(new StudentTransport
        {
            TenantId = fixture.TenantId, ClientRequestId = Guid.NewGuid(),
            StudentId = enrollment.StudentId, StudentEnrollmentId = enrollment.Id,
            RouteVehicleAssignmentId = routeLink.Id, RouteId = route.Id,
            VehicleId = fixture.VehicleId, PickupStopId = pickup.Id, DropStopId = drop.Id,
            StartDate = today, EndDate = today.AddDays(1),
            State = TransportAssignmentState.Active, MonthlyFare = 0m
        });
        await db.SaveChangesAsync();
        var service = TripService(db, fixture);
        var planned = await service.PlanAsync(new PlanTransportTripRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RouteReference = fixture.RouteReference,
            VehicleReference = fixture.VehicleReference, OperatingDate = today,
            Direction = "Pickup"
        });
        Assert.True(planned.Success, planned.Message);
        var dispatchKey = Guid.NewGuid();
        var dispatched = await service.DispatchAsync(planned.Data!.Reference,
            new DispatchTransportTripRequestDto
            {
                ClientRequestId = dispatchKey, RowVersion = planned.Data.RowVersion
            });
        Assert.True(dispatched.Success, dispatched.Message);
        var studentCode = await db.Students.Where(x => x.Id == enrollment.StudentId)
            .Select(x => x.StudentCode).SingleAsync();
        return new DispatchedTripFixture(fixture, planned.Data.Reference, dispatchKey, studentCode);
    }

    private sealed class RestrictedTransportManager(long tenant, long user) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["TransportManager"];
        public bool IsInRole(string role) => role == "TransportManager";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
