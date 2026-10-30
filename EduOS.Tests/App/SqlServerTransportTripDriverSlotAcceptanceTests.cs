using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server acceptance of unique current-driver ownership, safe handover
/// and historical trip-slot immutability across different vehicles.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task SQL_unique_current_driver_assignment_prevents_two_vehicle_claims()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var first = await PlannedTripAsync(connection);
        var second = await PlanSecondVehicleForSameDriverAsync(first);
        long secondVehicleId;
        long driverId;
        await using (var setup = ScopedContext(first.Fixture.Options,
            first.Fixture.TenantId, first.Fixture.UserId))
        {
            secondVehicleId = await setup.TransportTrips.AsNoTracking()
                .Where(x => x.TenantId == first.Fixture.TenantId && x.PublicId == second.Reference)
                .Select(x => x.VehicleId).SingleAsync();
            driverId = await setup.VehicleDriverAssignments.AsNoTracking()
                .Where(x => x.TenantId == first.Fixture.TenantId &&
                    x.VehicleId == first.Fixture.VehicleId && x.IsCurrent && !x.IsDeleted)
                .Select(x => x.TransportDriverId).SingleAsync();
            setup.VehicleDriverAssignments.Add(new VehicleDriverAssignment
            {
                TenantId = first.Fixture.TenantId, VehicleId = secondVehicleId,
                TransportDriverId = driverId, EffectiveFrom = DateOnly.FromDateTime(
                    TimeProvider.System.GetLocalNow().DateTime), IsCurrent = true
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => setup.SaveChangesAsync());
        }

        await using var verify = ScopedContext(first.Fixture.Options,
            first.Fixture.TenantId, first.Fixture.UserId);
        Assert.Equal(1, await verify.VehicleDriverAssignments.AsNoTracking().CountAsync(x =>
            x.TenantId == first.Fixture.TenantId && x.TransportDriverId == driverId &&
            x.IsCurrent && !x.IsDeleted));
        var denied = await TripService(verify, first.Fixture).DispatchAsync(
            second.Reference, new DispatchTransportTripRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = second.RowVersion
            });
        Assert.False(denied.Success);
        Assert.Equal(409, denied.StatusCode);
        await AssertPlannedAndEmptyAsync(second);
    }

    [Fact]
    public async Task Completed_driver_slot_remains_reserved_after_driver_handover()
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

        await HandoverDriverToSecondVehicleAsync(first, second);

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

    [Fact]
    public async Task Driver_handover_between_planned_vehicles_blocks_old_dispatch_and_allows_new()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var first = await PlannedTripAsync(connection);
        var second = await PlanSecondVehicleForSameDriverAsync(first);
        await HandoverDriverToSecondVehicleAsync(first, second);

        await using (var oldDb = ScopedContext(first.Fixture.Options,
            first.Fixture.TenantId, first.Fixture.UserId))
        {
            var denied = await TripService(oldDb, first.Fixture).DispatchAsync(
                first.Reference, new DispatchTransportTripRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = first.RowVersion
                });
            Assert.False(denied.Success);
            Assert.Equal(409, denied.StatusCode);
        }
        await AssertPlannedAndEmptyAsync(first);

        await using (var newDb = ScopedContext(first.Fixture.Options,
            first.Fixture.TenantId, first.Fixture.UserId))
        {
            var accepted = await TripService(newDb, first.Fixture).DispatchAsync(
                second.Reference, new DispatchTransportTripRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = second.RowVersion
                });
            Assert.True(accepted.Success, accepted.Message);
        }
        await using var verify = ScopedContext(first.Fixture.Options,
            first.Fixture.TenantId, first.Fixture.UserId);
        var trip = await verify.TransportTrips.AsNoTracking().SingleAsync(x =>
            x.TenantId == first.Fixture.TenantId && x.PublicId == second.Reference);
        Assert.Equal("Dispatched", trip.State);
        Assert.Equal(1, trip.RiderCount);
        Assert.Equal(1, await verify.TransportTripRiders.AsNoTracking().CountAsync(x =>
            x.TenantId == first.Fixture.TenantId && x.TransportTripId == trip.Id));
    }

    private static async Task HandoverDriverToSecondVehicleAsync(
        DispatchedTripFixture first, DispatchedTripFixture second)
    {
        var tenant = first.Fixture.TenantId;
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        await using var db = ScopedContext(first.Fixture.Options, tenant, first.Fixture.UserId);
        var old = await db.VehicleDriverAssignments.SingleAsync(x =>
            x.TenantId == tenant && x.VehicleId == first.Fixture.VehicleId &&
            x.IsCurrent && !x.IsDeleted);
        var driverId = old.TransportDriverId;
        var newVehicleId = await db.TransportTrips.AsNoTracking().Where(x =>
            x.TenantId == tenant && x.PublicId == second.Reference)
            .Select(x => x.VehicleId).SingleAsync();
        old.IsCurrent = false;
        old.EffectiveTo = today; // Inclusive historical end; never precede EffectiveFrom.
        await db.SaveChangesAsync();
        db.VehicleDriverAssignments.Add(new VehicleDriverAssignment
        {
            TenantId = tenant, VehicleId = newVehicleId, TransportDriverId = driverId,
            EffectiveFrom = today, EffectiveTo = today.AddDays(1), IsCurrent = true
        });
        await db.SaveChangesAsync();
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
