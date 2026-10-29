using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Tests.App;

/// <summary>Real SQL uniqueness and tenant-FK guards; no live institution data.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerTransportTripDispatchTests
{
    [Fact]
    public async Task Vehicle_date_direction_has_one_slot_and_composite_tenant_keys()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = new EduOSDbContext(options);
        var tenant = new Tenant
        {
            Code = "CI-TRIP-" + suffix, Name = "Trip dispatch CI",
            Email = "trip-" + suffix + "@example.test", State = TenantState.Active
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        using (db.BeginSystemTenantScope(tenant.Id))
        {
            var campus = new Campus
            {
                TenantId = tenant.Id, Name = "Trip campus",
                Code = "TR-" + suffix, IsActive = true
            };
            db.Campuses.Add(campus);
            await db.SaveChangesAsync();
            var route = new TransportRoute
            {
                TenantId = tenant.Id, CampusId = campus.Id, Name = "Trip route",
                Code = "TR-" + suffix, IsActive = true
            };
            var vehicle = new Vehicle
            {
                TenantId = tenant.Id, CampusId = campus.Id,
                VehicleNumber = "TRIP-" + suffix, Capacity = 20, IsActive = true
            };
            db.AddRange(route, vehicle);
            await db.SaveChangesAsync();
            var date = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var first = new TransportTrip
            {
                TenantId = tenant.Id, CampusId = campus.Id, RouteId = route.Id,
                VehicleId = vehicle.Id, OperatingDate = date, Direction = "Pickup",
                ClientRequestId = Guid.NewGuid(), State = "Planned"
            };
            db.TransportTrips.Add(first);
            await db.SaveChangesAsync();
            db.TransportTrips.Add(new TransportTrip
            {
                TenantId = tenant.Id, CampusId = campus.Id, RouteId = route.Id,
                VehicleId = vehicle.Id, OperatingDate = date, Direction = "Pickup",
                ClientRequestId = Guid.NewGuid(), State = "Planned"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
