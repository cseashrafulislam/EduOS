using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

public sealed class SqlServerTransportIdentityIntegrationTests
{
    [Fact]
    public async Task Normalized_vehicle_and_license_uniqueness_respects_tenant_and_soft_deletion()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenant1, tenant2;
        await using (var seed = new EduOSDbContext(options))
        {
            var first = new Tenant { Code = "CI-TV-" + suffix, Name = "Transport CI 1",
                Email = "tv1-" + suffix + "@example.test", State = TenantState.Active };
            var second = new Tenant { Code = "CI-TW-" + suffix, Name = "Transport CI 2",
                Email = "tv2-" + suffix + "@example.test", State = TenantState.Active };
            seed.Tenants.AddRange(first, second);
            await seed.SaveChangesAsync();
            tenant1 = first.Id;
            tenant2 = second.Id;
        }

        await using var db = ScopedContext(options, tenant1);
        var vehicleNumber = "CI-" + suffix.ToUpperInvariant();
        var licenseNumber = "LIC-" + suffix.ToUpperInvariant();
        var vehicle = new Vehicle { TenantId = tenant1, VehicleNumber = vehicleNumber, Capacity = 20 };
        var driver = new TransportDriver { TenantId = tenant1, FullName = "Driver One", LicenseNumber = licenseNumber };
        db.Vehicles.Add(vehicle);
        db.TransportDrivers.Add(driver);
        await db.SaveChangesAsync();
        Assert.Equal(8, vehicle.RowVersion.Length);

        var duplicateVehicle = new Vehicle { TenantId = tenant1,
            VehicleNumber = "  " + vehicleNumber.ToLowerInvariant() + " ", Capacity = 20 };
        db.Vehicles.Add(duplicateVehicle);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.Entry(duplicateVehicle).State = EntityState.Detached;

        var duplicateDriver = new TransportDriver { TenantId = tenant1,
            FullName = "Driver Two", LicenseNumber = " " + licenseNumber.ToLowerInvariant() + "  " };
        db.TransportDrivers.Add(duplicateDriver);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.Entry(duplicateDriver).State = EntityState.Detached;

        vehicle.IsDeleted = true;
        driver.IsDeleted = true;
        await db.SaveChangesAsync();
        db.Vehicles.Add(new Vehicle { TenantId = tenant1, VehicleNumber = vehicleNumber, Capacity = 20 });
        db.TransportDrivers.Add(new TransportDriver { TenantId = tenant1,
            FullName = "Replacement Driver", LicenseNumber = licenseNumber });
        await db.SaveChangesAsync();

        await using var other = ScopedContext(options, tenant2);
        other.Vehicles.Add(new Vehicle { TenantId = tenant2, VehicleNumber = vehicleNumber, Capacity = 20 });
        other.TransportDrivers.Add(new TransportDriver { TenantId = tenant2,
            FullName = "Other Campus Driver", LicenseNumber = licenseNumber });
        await other.SaveChangesAsync();
    }

    [Fact]
    public async Task Vehicle_uses_real_sql_server_rowversion_for_conflicting_updates()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, vehicleId;
        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-TX-" + suffix, Name = "Concurrency CI",
                Email = "tx-" + suffix + "@example.test", State = TenantState.Active };
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
            using (seed.BeginSystemTenantScope(tenantId))
            {
                var vehicle = new Vehicle { TenantId = tenantId, VehicleNumber = "RV-" + suffix, Capacity = 25 };
                seed.Vehicles.Add(vehicle);
                await seed.SaveChangesAsync();
                vehicleId = vehicle.Id;
            }
        }
        await using var first = ScopedContext(options, tenantId);
        await using var second = ScopedContext(options, tenantId);
        var a = await first.Vehicles.SingleAsync(x => x.Id == vehicleId);
        var b = await second.Vehicles.SingleAsync(x => x.Id == vehicleId);
        Assert.Equal(8, a.RowVersion.Length);
        a.Capacity = 26;
        await first.SaveChangesAsync();
        b.Capacity = 27;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static EduOSDbContext ScopedContext(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
}
