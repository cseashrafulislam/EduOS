using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
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
    public async Task Parallel_identical_transport_command_keys_can_commit_only_one_receipt_on_sql_server()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId;
        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-IK-" + suffix, Name = "Parallel receipt CI",
                Email = "ik-" + suffix + "@example.test", State = TenantState.Active };
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
        }
        var key = Guid.NewGuid();
        async Task<bool> InsertAsync()
        {
            await using var db = ScopedContext(options, tenantId);
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                TenantId = tenantId, Scope = "transport.upcoming-driver-change",
                RequestId = key, TargetId = 77, CompletedByUserId = 7,
                RequestHash = new string('a', 64), ResultHash = new string('b', 64),
                ResultJson = "{}", CompletedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.MaxValue
            });
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException) { return false; }
        }

        var results = await Task.WhenAll(InsertAsync(), InsertAsync());
        Assert.Single(results, committed => committed);
        await using var verify = ScopedContext(options, tenantId);
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x =>
            x.Scope == "transport.upcoming-driver-change" && x.RequestId == key));
    }

    [Fact]
    public async Task Command_receipt_key_is_unique_per_tenant_and_scope_on_sql_server()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId;
        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-ID-" + suffix, Name = "Request-key CI",
                Email = "id-" + suffix + "@example.test", State = TenantState.Active };
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
        }
        var requestId = Guid.NewGuid();
        var first = new IdempotencyRecord { TenantId = tenantId,
            Scope = "transport.route-vehicle-change", RequestId = requestId,
            RequestHash = new string('a', 64), ResultHash = new string('b', 64),
            TargetId = 123, CompletedByUserId = 456, CompletedAt = DateTime.UtcNow,
            ResultJson = "{\"Success\":true}", ExpiresAt = DateTime.MaxValue };
        await using (var db = ScopedContext(options, tenantId))
        {
            db.IdempotencyRecords.Add(first);
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(options, tenantId))
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord { TenantId = tenantId,
                Scope = first.Scope, RequestId = requestId, ExpiresAt = DateTime.MaxValue });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = ScopedContext(options, tenantId))
        {
            var persisted = await db.IdempotencyRecords.SingleAsync(x => x.RequestId == requestId);
            Assert.Equal(123, persisted.TargetId);
            Assert.Equal(456, persisted.CompletedByUserId);
            Assert.Equal(first.ResultJson, persisted.ResultJson);
        }
    }

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

        // Unlicensed drivers must not collide on the computed NULL index key.
        db.TransportDrivers.AddRange(
            new TransportDriver { TenantId = tenant1, FullName = "Unlicensed Driver One" },
            new TransportDriver { TenantId = tenant1, FullName = "Unlicensed Driver Two" });
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

    [Fact]
    public async Task Route_vehicle_assignment_has_sql_rowversion_and_rejects_a_second_current_vehicle_link()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, linkId, vehicleId, alternateRouteId;
        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-RV-" + suffix, Name = "Route/vehicle concurrency CI",
                Email = "rv-" + suffix + "@example.test", State = TenantState.Active };
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
            using (seed.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Transport test campus",
                    Code = "RV-C", IsActive = true };
                seed.Campuses.Add(campus);
                await seed.SaveChangesAsync();
                var firstRoute = new Route { TenantId = tenantId, CampusId = campus.Id,
                    Name = "Route one", Code = "RV-1", IsActive = true };
                var alternateRoute = new Route { TenantId = tenantId, CampusId = campus.Id,
                    Name = "Route two", Code = "RV-2", IsActive = true };
                var vehicle = new Vehicle { TenantId = tenantId, CampusId = campus.Id,
                    VehicleNumber = "CI-RV-BUS-" + suffix, Capacity = 20, IsActive = true };
                seed.AddRange(firstRoute, alternateRoute, vehicle);
                await seed.SaveChangesAsync();
                alternateRouteId = alternateRoute.Id;
                vehicleId = vehicle.Id;
                var link = new RouteVehicleAssignment { TenantId = tenantId, RouteId = firstRoute.Id,
                    VehicleId = vehicleId, EffectiveFrom = new DateOnly(2027, 1, 10),
                    EffectiveTo = new DateOnly(2027, 1, 20), IsCurrent = true };
                seed.RouteVehicleAssignments.Add(link);
                await seed.SaveChangesAsync();
                linkId = link.Id;
                Assert.Equal(8, link.RowVersion.Length);
            }
        }
        await using var first = ScopedContext(options, tenantId);
        await using var second = ScopedContext(options, tenantId);
        var a = await first.RouteVehicleAssignments.SingleAsync(x => x.Id == linkId);
        var b = await second.RouteVehicleAssignments.SingleAsync(x => x.Id == linkId);
        a.EffectiveTo = new DateOnly(2027, 1, 25);
        await first.SaveChangesAsync();
        b.EffectiveTo = new DateOnly(2027, 1, 26);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        // The deployed filtered index deliberately reserves at most one current
        // route link per vehicle; a future non-overlapping link must not bypass it.
        await using var competitor = ScopedContext(options, tenantId);
        competitor.RouteVehicleAssignments.Add(new RouteVehicleAssignment
        {
            TenantId = tenantId, RouteId = alternateRouteId, VehicleId = vehicleId,
            EffectiveFrom = new DateOnly(2027, 2, 1), EffectiveTo = new DateOnly(2027, 2, 10),
            IsCurrent = true
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => competitor.SaveChangesAsync());
        await using var verify = ScopedContext(options, tenantId);
        Assert.Equal(new DateOnly(2027, 1, 25), await verify.RouteVehicleAssignments
            .Where(x => x.Id == linkId).Select(x => x.EffectiveTo).SingleAsync());
    }

    [Fact]
    public async Task Driver_assignment_has_sql_rowversion_and_prevents_a_second_current_driver_link()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, linkId, driverId, alternateVehicleId;
        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-DL-" + suffix, Name = "Driver assignment concurrency CI",
                Email = "dl-" + suffix + "@example.test", State = TenantState.Active };
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
            using (seed.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "DL-C",
                    Name = "Driver campus", IsActive = true };
                seed.Campuses.Add(campus);
                await seed.SaveChangesAsync();
                var firstVehicle = new Vehicle { TenantId = tenantId, CampusId = campus.Id,
                    VehicleNumber = "CI-DL-ONE-" + suffix, Capacity = 30, IsActive = true };
                var otherVehicle = new Vehicle { TenantId = tenantId, CampusId = campus.Id,
                    VehicleNumber = "CI-DL-TWO-" + suffix, Capacity = 30, IsActive = true };
                var driver = new TransportDriver { TenantId = tenantId, FullName = "Driver CI",
                    LicenseNumber = "CI-DL-LIC-" + suffix, LicenseExpiresOn = new DateOnly(2028, 1, 1),
                    IsActive = true };
                seed.AddRange(firstVehicle, otherVehicle, driver);
                await seed.SaveChangesAsync();
                driverId = driver.Id;
                alternateVehicleId = otherVehicle.Id;
                var link = new VehicleDriverAssignment { TenantId = tenantId,
                    VehicleId = firstVehicle.Id, TransportDriverId = driverId,
                    EffectiveFrom = new DateOnly(2027, 1, 10),
                    EffectiveTo = new DateOnly(2027, 1, 20), IsCurrent = true };
                seed.VehicleDriverAssignments.Add(link);
                await seed.SaveChangesAsync();
                linkId = link.Id;
                Assert.Equal(8, link.RowVersion.Length);
            }
        }
        await using var first = ScopedContext(options, tenantId);
        await using var second = ScopedContext(options, tenantId);
        var a = await first.VehicleDriverAssignments.SingleAsync(x => x.Id == linkId);
        var b = await second.VehicleDriverAssignments.SingleAsync(x => x.Id == linkId);
        a.EffectiveTo = new DateOnly(2027, 1, 25);
        await first.SaveChangesAsync();
        b.EffectiveTo = new DateOnly(2027, 1, 26);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var competitor = ScopedContext(options, tenantId);
        competitor.VehicleDriverAssignments.Add(new VehicleDriverAssignment
        {
            TenantId = tenantId, VehicleId = alternateVehicleId, TransportDriverId = driverId,
            EffectiveFrom = new DateOnly(2027, 2, 1), EffectiveTo = new DateOnly(2027, 2, 10),
            IsCurrent = true
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => competitor.SaveChangesAsync());
        await using var verify = ScopedContext(options, tenantId);
        Assert.Equal(new DateOnly(2027, 1, 25), await verify.VehicleDriverAssignments
            .Where(x => x.Id == linkId).Select(x => x.EffectiveTo).SingleAsync());
    }

    private static EduOSDbContext ScopedContext(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
}
