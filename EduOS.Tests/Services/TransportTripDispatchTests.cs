using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Transport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportTripDispatchTests
{
    [Fact]
    public async Task Planning_is_idempotent_and_rejects_reused_key_and_vehicle_slot_collision()
    {
        await using var db = Context(101);
        var (route, vehicle) = await SeedAsync(db);
        var service = Service(db, new User(101, "TenantAdmin"));
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var request = new PlanTransportTripRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RouteReference = route.PublicId,
            VehicleReference = vehicle.PublicId, OperatingDate = today, Direction = "Pickup"
        };
        var first = await service.PlanAsync(request);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.State.Should().Be("Planned");
        var replay = await service.PlanAsync(request);
        replay.Success.Should().BeTrue(replay.Message);
        replay.Data!.Reference.Should().Be(first.Data.Reference);
        db.TransportTrips.Count().Should().Be(1);
        request.Direction = "Drop";
        (await service.PlanAsync(request)).StatusCode.Should().Be(409);
        db.TransportTrips.Count().Should().Be(1);
    }

    [Fact]
    public async Task Dispatch_fails_closed_without_driver_and_does_not_create_rider_snapshot()
    {
        await using var db = Context(101);
        var (route, vehicle) = await SeedAsync(db);
        var service = Service(db, new User(101, "TenantAdmin"));
        var planned = await service.PlanAsync(new PlanTransportTripRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RouteReference = route.PublicId,
            VehicleReference = vehicle.PublicId,
            OperatingDate = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime),
            Direction = "Pickup"
        });
        planned.Success.Should().BeTrue();
        var trip = await db.TransportTrips.SingleAsync();
        trip.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await db.SaveChangesAsync();
        var rejected = await service.DispatchAsync(planned.Data!.Reference,
            new DispatchTransportTripRequestDto
            {
                ClientRequestId = Guid.NewGuid(),
                RowVersion = Convert.ToBase64String(trip.RowVersion)
            });
        rejected.StatusCode.Should().Be(409);
        trip.State.Should().Be("Planned");
        db.TransportTripRiders.Should().BeEmpty();
    }

    [Fact]
    public async Task Role_tenant_and_campus_boundaries_are_enforced_on_trip_endpoints()
    {
        await using var db = Context(101);
        var (route, vehicle) = await SeedAsync(db);
        var admin = Service(db, new User(101, "TenantAdmin"));
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var planned = await admin.PlanAsync(new PlanTransportTripRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RouteReference = route.PublicId,
            VehicleReference = vehicle.PublicId, OperatingDate = today, Direction = "Pickup"
        });
        planned.Success.Should().BeTrue();
        var manager = Service(db, new User(101, "TransportManager"));
        (await manager.GetAsync(today, null, 1, 25)).Data!.Items.Should().BeEmpty();
        (await manager.GetRidersAsync(planned.Data!.Reference, 1, 25)).StatusCode.Should().Be(404);
        (await manager.PlanAsync(new PlanTransportTripRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RouteReference = route.PublicId,
            VehicleReference = vehicle.PublicId, OperatingDate = today, Direction = "Drop"
        })).StatusCode.Should().Be(403);
        var otherTenant = Service(db, new User(202, "TenantAdmin"));
        (await otherTenant.GetAsync(today, null, 1, 25)).Data!.Items.Should().BeEmpty();
        (await otherTenant.GetRidersAsync(planned.Data.Reference, 1, 25)).StatusCode.Should().Be(404);
        var student = Service(db, new User(101, "Student"));
        (await student.GetAsync(today, null, 1, 25)).StatusCode.Should().Be(403);
        (await student.ExportRidersCsvAsync(planned.Data.Reference)).StatusCode.Should().Be(403);
    }

    [Fact]
    public void Trip_schema_has_composite_tenant_foreign_keys_and_unique_dispatch_slots()
    {
        using var db = Context(101);
        var trip = db.Model.FindEntityType(typeof(TransportTrip));
        var rider = db.Model.FindEntityType(typeof(TransportTripRider));
        trip.Should().NotBeNull();
        rider.Should().NotBeNull();
        trip!.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "VehicleId", "OperatingDate", "Direction" }));
        trip.GetForeignKeys().Should().Contain(x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "TenantId", "CampusId" }));
        rider!.GetForeignKeys().Should().Contain(x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "TenantId", "TransportTripId" }));
        rider.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "TransportTripId", "StudentTransportId" }));
    }

    private static TransportTripService Service(EduOSDbContext db, ICurrentUserService user) =>
        new(db, user, TimeProvider.System, NullLogger<TransportTripService>.Instance);

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transport-trip-dispatch-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static async Task<(Route route, Vehicle vehicle)> SeedAsync(EduOSDbContext db)
    {
        var campus = new Campus { TenantId = 101, Name = "Transport campus", Code = "TRIP", IsActive = true };
        db.Campuses.Add(campus);
        await db.SaveChangesAsync();
        var route = new Route { TenantId = 101, CampusId = campus.Id, Name = "North", Code = "NORTH", IsActive = true };
        var vehicle = new Vehicle { TenantId = 101, CampusId = campus.Id, VehicleNumber = "TRIP-101", Capacity = 30, IsActive = true };
        db.AddRange(route, vehicle);
        await db.SaveChangesAsync();
        db.RouteVehicleAssignments.Add(new RouteVehicleAssignment
        {
            TenantId = 101, RouteId = route.Id, VehicleId = vehicle.Id,
            EffectiveFrom = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime),
            IsCurrent = true
        });
        await db.SaveChangesAsync();
        return (route, vehicle);
    }

    private sealed class User(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 42;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
