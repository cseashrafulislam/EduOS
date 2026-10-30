using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

// Uses the real disposable SQL fixture and tenant-scoped DbContexts.
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task NoShow_batch_is_atomic_replayable_and_completion_safe()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        var key = Guid.NewGuid();
        await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var service = TripService(db, state.Fixture);
        var preview = await service.GetNoShowPreviewAsync(state.Reference);
        Assert.True(preview.Success, preview.Message);
        Assert.Equal(1, preview.Data!.AwaitingCount);
        var request = new MarkTransportTripAwaitingNoShowRequestDto
        {
            ClientRequestId = key, ExpectedAwaitingCount = 1
        };
        var saved = await service.MarkAwaitingNoShowAsync(state.Reference, request);
        Assert.True(saved.Success, saved.Message);
        Assert.Equal(1, saved.Data!.RecordedCount);
        var replay = await service.MarkAwaitingNoShowAsync(state.Reference, request);
        Assert.True(replay.Success, replay.Message);
        Assert.True(replay.Data!.AlreadyRecorded);
        var trip = await db.TransportTrips.AsNoTracking().SingleAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.PublicId == state.Reference);
        var completed = await service.CompleteAsync(state.Reference,
            new CompleteTransportTripRequestDto
            {
                ClientRequestId = Guid.NewGuid(),
                RowVersion = Convert.ToBase64String(trip.RowVersion)
            });
        Assert.True(completed.Success, completed.Message);
        Assert.True((await service.MarkAwaitingNoShowAsync(state.Reference, request)).Success);
        Assert.Equal(409, (await service.MarkAwaitingNoShowAsync(state.Reference,
            new MarkTransportTripAwaitingNoShowRequestDto
            {
                ClientRequestId = Guid.NewGuid(), ExpectedAwaitingCount = 1
            })).StatusCode);
        var events = await db.TransportTripAttendanceEvents.AsNoTracking()
            .Where(x => x.TenantId == state.Fixture.TenantId).ToListAsync();
        Assert.Single(events);
        Assert.Equal("NoShow", events[0].EventType);
    }

    [Fact]
    public async Task NoShow_batch_rejects_stale_count_without_touching_boarded_rider()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var service = TripService(db, state.Fixture);
        var boarded = await service.RecordAttendanceAsync(state.Reference,
            new RecordTransportTripAttendanceRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode,
                EventType = "Boarded"
            });
        Assert.True(boarded.Success, boarded.Message);
        var denied = await service.MarkAwaitingNoShowAsync(state.Reference,
            new MarkTransportTripAwaitingNoShowRequestDto
            {
                ClientRequestId = Guid.NewGuid(), ExpectedAwaitingCount = 1
            });
        Assert.Equal(409, denied.StatusCode);
        var preview = await service.GetNoShowPreviewAsync(state.Reference);
        Assert.Equal(0, preview.Data!.AwaitingCount);
        Assert.Equal(1, preview.Data.BoardedCount);
        var tripId = await db.TransportTrips.Where(x => x.PublicId == state.Reference)
            .Select(x => x.Id).SingleAsync();
        var riderId = await db.TransportTripRiders.Where(x => x.TransportTripId == tripId)
            .Select(x => x.Id).SingleAsync();
        Assert.Equal("Boarded", Assert.Single(await db.TransportTripAttendanceEvents.AsNoTracking()
            .Where(x => x.TransportTripRiderId == riderId).ToListAsync()).EventType);
    }

    [Fact]
    public async Task NoShow_batch_denies_cross_tenant_guessed_trip_reference()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        var otherTenant = state.Fixture.TenantId + 900000000L;
        await using var db = ScopedContext(state.Fixture.Options, otherTenant, state.Fixture.UserId);
        var service = new EduOS.Service.Services.Transport.TransportTripService(db,
            new TenantAdmin(otherTenant, state.Fixture.UserId),
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<
                EduOS.Service.Services.Transport.TransportTripService>.Instance);
        var denied = await service.MarkAwaitingNoShowAsync(state.Reference,
            new MarkTransportTripAwaitingNoShowRequestDto
            {
                ClientRequestId = Guid.NewGuid(), ExpectedAwaitingCount = 1
            });
        Assert.False(denied.Success);
        Assert.Equal(404, denied.StatusCode);
    }
}
