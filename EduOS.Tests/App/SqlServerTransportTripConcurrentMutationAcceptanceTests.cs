using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server in-flight mutation acceptance for Transport trip dispatch.
/// A separate writer commits authoritative eligibility/capacity changes while
/// dispatch is in progress; no shared DbContext or in-memory database is used.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task In_flight_reservation_insertion_must_not_freeze_an_overcapacity_manifest()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await PlannedTripAsync(connection);
        var extraRequest = Guid.NewGuid();

        var denied = await DispatchWhileMutationCommitsAsync(state, async writer =>
        {
            var existing = await writer.StudentTransports.AsNoTracking().SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.VehicleId == state.Fixture.VehicleId &&
                x.State == TransportAssignmentState.Active);
            var second = await writer.StudentEnrollments.AsNoTracking().SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.PublicId == state.Fixture.Enrollments[1]);
            writer.StudentTransports.Add(new StudentTransport
            {
                TenantId = state.Fixture.TenantId, ClientRequestId = extraRequest,
                StudentId = second.StudentId, StudentEnrollmentId = second.Id,
                RouteVehicleAssignmentId = existing.RouteVehicleAssignmentId,
                RouteId = existing.RouteId, VehicleId = existing.VehicleId,
                PickupStopId = existing.PickupStopId, DropStopId = existing.DropStopId,
                StartDate = existing.StartDate, EndDate = existing.EndDate,
                State = TransportAssignmentState.Active, MonthlyFare = 0m
            });
        });

        Assert.False(denied.Success);
        Assert.Equal(409, denied.StatusCode);
        await AssertPlannedAndEmptyAsync(state);

        await using (var repair = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var extra = await repair.StudentTransports.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.ClientRequestId == extraRequest);
            extra.State = TransportAssignmentState.Cancelled;
            await repair.SaveChangesAsync();
        }
        await AssertDispatchRecoversAsync(state);
    }

    [Fact]
    public async Task In_flight_driver_licence_revocation_must_prevent_dispatch_and_snapshot()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await PlannedTripAsync(connection);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        long driverId = 0;

        var denied = await DispatchWhileMutationCommitsAsync(state, async writer =>
        {
            var link = await writer.VehicleDriverAssignments.AsNoTracking().SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.VehicleId == state.Fixture.VehicleId &&
                x.IsCurrent && !x.IsDeleted);
            driverId = link.TransportDriverId;
            var driver = await writer.TransportDrivers.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.Id == driverId);
            driver.LicenseExpiresOn = today.AddDays(-1);
        });

        Assert.False(denied.Success);
        Assert.Equal(409, denied.StatusCode);
        await AssertPlannedAndEmptyAsync(state);

        await using (var repair = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var driver = await repair.TransportDrivers.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.Id == driverId);
            driver.LicenseExpiresOn = today.AddDays(60);
            await repair.SaveChangesAsync();
        }
        await AssertDispatchRecoversAsync(state);
    }

    [Fact]
    public async Task In_flight_route_vehicle_revocation_must_prevent_dispatch_and_snapshot()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await PlannedTripAsync(connection);
        long linkId = 0;

        var denied = await DispatchWhileMutationCommitsAsync(state, async writer =>
        {
            var link = await writer.RouteVehicleAssignments.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.VehicleId == state.Fixture.VehicleId &&
                x.IsCurrent && !x.IsDeleted);
            linkId = link.Id;
            link.IsCurrent = false;
        });

        Assert.False(denied.Success);
        Assert.Equal(409, denied.StatusCode);
        await AssertPlannedAndEmptyAsync(state);

        await using (var repair = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var link = await repair.RouteVehicleAssignments.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.Id == linkId);
            link.IsCurrent = true;
            await repair.SaveChangesAsync();
        }
        await AssertDispatchRecoversAsync(state);
    }

    private static async Task<ApiResponse<TransportTripRowDto>> DispatchWhileMutationCommitsAsync(
        DispatchedTripFixture state, Func<EduOSDbContext, Task> mutate)
    {
        await using var writer = ScopedContext(state.Fixture.Options,
            state.Fixture.TenantId, state.Fixture.UserId);
        await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await mutate(writer);
        await writer.SaveChangesAsync();

        // The authoritative change is still uncommitted when a separate SQL
        // connection begins dispatch. The commit releases the writer locks.
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var contender = Task.Run(async () =>
        {
            started.TrySetResult(true);
            await using var reader = ScopedContext(state.Fixture.Options,
                state.Fixture.TenantId, state.Fixture.UserId);
            return await TripService(reader, state.Fixture).DispatchAsync(state.Reference,
                new DispatchTransportTripRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = state.RowVersion
                });
        });
        await started.Task;
        await Task.Delay(200);
        await transaction.CommitAsync();
        return await contender.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async Task AssertPlannedAndEmptyAsync(DispatchedTripFixture state)
    {
        await using var verify = ScopedContext(state.Fixture.Options,
            state.Fixture.TenantId, state.Fixture.UserId);
        var trip = await verify.TransportTrips.AsNoTracking().SingleAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.PublicId == state.Reference);
        Assert.Equal("Planned", trip.State);
        Assert.Equal(0, trip.RiderCount);
        Assert.Equal(0, await verify.TransportTripRiders.AsNoTracking().CountAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.TransportTripId == trip.Id));
    }

    private static async Task AssertDispatchRecoversAsync(DispatchedTripFixture state)
    {
        await using var db = ScopedContext(state.Fixture.Options,
            state.Fixture.TenantId, state.Fixture.UserId);
        var result = await TripService(db, state.Fixture).DispatchAsync(state.Reference,
            new DispatchTransportTripRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = state.RowVersion
            });
        Assert.True(result.Success, "A repaired trip must dispatch: " + result.StatusCode);
        var trip = await db.TransportTrips.AsNoTracking().SingleAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.PublicId == state.Reference);
        Assert.Equal("Dispatched", trip.State);
        Assert.Equal(1, trip.RiderCount);
        Assert.Equal(1, await db.TransportTripRiders.AsNoTracking().CountAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.TransportTripId == trip.Id));
    }
}
