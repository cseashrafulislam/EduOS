using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Schedule_stops_on_uncovered_date_then_replays_committed_days_without_duplicates()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await PlannedTripAsync(connection);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var request = new ScheduleTransportTripsRequestDto
        {
            ClientRequestId = Guid.NewGuid(),
            RouteReference = state.Fixture.RouteReference,
            VehicleReference = state.Fixture.VehicleReference,
            StartDate = today.AddDays(1), EndDate = today.AddDays(2),
            Direction = "Pickup"
        };

        async Task<EduOS.Core.Common.ApiResponse<TransportTripScheduleResultDto>> RunAsync()
        {
            await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
            return await TripService(db, state.Fixture).ScheduleAsync(request);
        }

        var partial = await RunAsync();
        Assert.True(partial.Success, partial.Message);
        Assert.Equal(207, partial.StatusCode);
        Assert.Equal(1, partial.Data!.NewlyPlanned);
        Assert.Equal(today.AddDays(2), partial.Data.FailedDate);
        Assert.Equal(409, partial.Data.FailureStatusCode);
        Assert.Single(partial.Data.Trips);

        await using (var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var route = await db.Routes.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.PublicId == request.RouteReference);
            var link = await db.RouteVehicleAssignments.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.RouteId == route.Id &&
                x.VehicleId == state.Fixture.VehicleId && x.IsCurrent && !x.IsDeleted);
            link.EffectiveTo = today.AddDays(2);
            await db.SaveChangesAsync();
        }

        var complete = await RunAsync();
        Assert.True(complete.Success, complete.Message);
        Assert.Equal(1, complete.Data!.AlreadyPlanned);
        Assert.Equal(1, complete.Data.NewlyPlanned);
        Assert.Null(complete.Data.FailedDate);
        Assert.Equal(2, complete.Data.Trips.Count);
        Assert.Equal(partial.Data.Trips[0].Reference, complete.Data.Trips[0].Reference);

        var replay = await RunAsync();
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(0, replay.Data!.NewlyPlanned);
        Assert.Equal(2, replay.Data.AlreadyPlanned);

        await using var verify = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var planned = await verify.TransportTrips.AsNoTracking().Where(x =>
            x.TenantId == state.Fixture.TenantId && x.VehicleId == state.Fixture.VehicleId &&
            x.OperatingDate >= request.StartDate && x.OperatingDate <= request.EndDate &&
            x.Direction == request.Direction).ToListAsync();
        Assert.Equal(2, planned.Count);
        Assert.All(planned, x => { Assert.Equal("Planned", x.State); Assert.Equal(0, x.RiderCount); });

        request.Direction = "Drop";
        var changed = await RunAsync();
        Assert.Equal(207, changed.StatusCode);
        Assert.Equal(409, changed.Data!.FailureStatusCode);
        Assert.Empty(changed.Data.Trips);
    }

    [Fact]
    public async Task Schedule_rejects_unbounded_ranges_and_foreign_tenant_ids()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await PlannedTripAsync(connection);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var request = new ScheduleTransportTripsRequestDto
        {
            ClientRequestId = Guid.NewGuid(),
            RouteReference = state.Fixture.RouteReference,
            VehicleReference = state.Fixture.VehicleReference,
            StartDate = today, EndDate = today.AddDays(7), Direction = "Pickup"
        };
        await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        Assert.Equal(400, (await TripService(db, state.Fixture).ScheduleAsync(request)).StatusCode);
        request.EndDate = today.AddDays(1);
        request.RouteReference = Guid.NewGuid();
        var guessed = await TripService(db, state.Fixture).ScheduleAsync(request);
        Assert.Equal(207, guessed.StatusCode);
        Assert.Equal(404, guessed.Data!.FailureStatusCode);
        Assert.Empty(guessed.Data.Trips);
        await using var foreignDb = ScopedContext(state.Fixture.Options, long.MaxValue, state.Fixture.UserId);
        var foreign = new EduOS.Service.Services.Transport.TransportTripService(foreignDb,
            new RestrictedTransportManager(long.MaxValue, state.Fixture.UserId),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EduOS.Service.Services.Transport.TransportTripService>.Instance);
        request.RouteReference = state.Fixture.RouteReference;
        var denied = await foreign.ScheduleAsync(request);
        Assert.Equal(207, denied.StatusCode);
        Assert.Equal(404, denied.Data!.FailureStatusCode);
        Assert.Empty(denied.Data.Trips);
    }
}
