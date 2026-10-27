using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Platform-owned SQL Server race coverage for the existing Transport indexes.
/// These tests prove database uniqueness, not service-level capacity or date-overlap safety.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerTransportConcurrentUniquenessTests
{
    [Fact]
    public async Task Parallel_vehicle_and_driver_registration_respects_normalized_tenant_uniqueness()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await SeedAsync(connection);
        var number = "CI-NEW-" + state.Suffix;
        var license = "CI-NEW-LIC-" + state.Suffix;

        await AssertOneCommitAsync(state, (db, contender) => db.Vehicles.Add(new Vehicle
        {
            TenantId = state.TenantId, CampusId = state.CampusId,
            VehicleNumber = contender == 0 ? number : "  " + number.ToLowerInvariant() + " ",
            Capacity = 20
        }));
        await AssertOneCommitAsync(state, (db, contender) => db.TransportDrivers.Add(new TransportDriver
        {
            TenantId = state.TenantId, FullName = "Concurrent driver " + contender,
            LicenseNumber = contender == 0 ? license : " " + license.ToLowerInvariant() + " "
        }));

        await using var verify = ScopedContext(state.Options, state.TenantId);
        Assert.Equal(1, await verify.Vehicles.CountAsync(x =>
            EF.Property<string>(x, "NormalizedVehicleNumber") == number.ToUpperInvariant()));
        Assert.Equal(1, await verify.TransportDrivers.CountAsync(x =>
            EF.Property<string>(x, "NormalizedLicenseNumber") == license.ToUpperInvariant()));
    }

    [Fact]
    public async Task Normalized_transport_identity_keys_are_tenant_scoped_and_reusable_after_soft_delete()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await SeedAsync(connection);

        string vehicleNumber, licenseNumber;
        await using (var original = ScopedContext(state.Options, state.TenantId))
        {
            vehicleNumber = (await original.Vehicles.AsNoTracking()
                .SingleAsync(x => x.Id == state.Vehicle1Id)).VehicleNumber;
            licenseNumber = (await original.TransportDrivers.AsNoTracking()
                .SingleAsync(x => x.Id == state.DriverId)).LicenseNumber!;
        }

        // The unique keys are tenant-scoped, not global.
        long otherTenantId;
        await using (var seed = new EduOSDbContext(state.Options))
        {
            var tenant = new Tenant
            {
                Code = "CI-OTHER-" + state.Suffix,
                Name = "Independent transport identity CI",
                Email = "other-" + state.Suffix + "@example.test",
                State = TenantState.Active
            };
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            otherTenantId = tenant.Id;
        }
        await using (var other = ScopedContext(state.Options, otherTenantId))
        {
            var campus = new Campus
            {
                TenantId = otherTenantId, Code = "OTHER-" + state.Suffix,
                Name = "Other tenant campus", IsActive = true
            };
            other.Campuses.Add(campus);
            await other.SaveChangesAsync();
            other.Vehicles.Add(new Vehicle
            {
                TenantId = otherTenantId, CampusId = campus.Id,
                VehicleNumber = "  " + vehicleNumber.ToLowerInvariant() + " ",
                Capacity = 20, IsActive = true
            });
            other.TransportDrivers.Add(new TransportDriver
            {
                TenantId = otherTenantId, FullName = "Other tenant driver",
                LicenseNumber = " " + licenseNumber.ToLowerInvariant() + " ",
                IsActive = true
            });
            await other.SaveChangesAsync();
        }

        // Soft deletion releases the identity within its original tenant;
        // historical rows remain stored and are never merged or renumbered.
        await using (var original = ScopedContext(state.Options, state.TenantId))
        {
            var vehicle = await original.Vehicles.SingleAsync(x => x.Id == state.Vehicle1Id);
            var driver = await original.TransportDrivers.SingleAsync(x => x.Id == state.DriverId);
            vehicle.IsDeleted = true;
            driver.IsDeleted = true;
            await original.SaveChangesAsync();
            original.Vehicles.Add(new Vehicle
            {
                TenantId = state.TenantId, CampusId = state.CampusId,
                VehicleNumber = " " + vehicleNumber.ToLowerInvariant() + " ",
                Capacity = 20, IsActive = true
            });
            original.TransportDrivers.Add(new TransportDriver
            {
                TenantId = state.TenantId, FullName = "Replacement driver",
                LicenseNumber = "  " + licenseNumber.ToLowerInvariant() + " ",
                IsActive = true
            });
            await original.SaveChangesAsync();
        }

        foreach (var tenantId in new[] { state.TenantId, otherTenantId })
        {
            await using var verify = ScopedContext(state.Options, tenantId);
            Assert.Equal(1, await verify.Vehicles.CountAsync(x =>
                !x.IsDeleted && EF.Property<string>(x, "NormalizedVehicleNumber") ==
                    vehicleNumber.ToUpperInvariant()));
            Assert.Equal(1, await verify.TransportDrivers.CountAsync(x =>
                !x.IsDeleted && EF.Property<string>(x, "NormalizedLicenseNumber") ==
                    licenseNumber.ToUpperInvariant()));
        }
    }

    [Fact]
    public async Task Parallel_current_route_and_driver_links_reject_competing_writers()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await SeedAsync(connection);
        var from = new DateOnly(2027, 1, 10);
        var to = new DateOnly(2027, 1, 20);

        await AssertOneCommitAsync(state, (db, contender) => db.RouteVehicleAssignments.Add(
            new RouteVehicleAssignment
            {
                TenantId = state.TenantId, RouteId = contender == 0 ? state.Route1Id : state.Route2Id,
                VehicleId = state.Vehicle1Id, EffectiveFrom = from, EffectiveTo = to,
                IsCurrent = true
            }));
        await AssertOneCommitAsync(state, (db, contender) => db.VehicleDriverAssignments.Add(
            new VehicleDriverAssignment
            {
                TenantId = state.TenantId,
                VehicleId = contender == 0 ? state.Vehicle1Id : state.Vehicle2Id,
                TransportDriverId = state.DriverId, EffectiveFrom = from, EffectiveTo = to,
                IsCurrent = true
            }));

        await using var verify = ScopedContext(state.Options, state.TenantId);
        Assert.Equal(1, await verify.RouteVehicleAssignments.CountAsync(x =>
            x.VehicleId == state.Vehicle1Id && x.IsCurrent && !x.IsDeleted));
        Assert.Equal(1, await verify.VehicleDriverAssignments.CountAsync(x =>
            x.TransportDriverId == state.DriverId && x.IsCurrent && !x.IsDeleted));
    }

    [Fact]
    public async Task Driver_rowversion_rejects_stale_second_context_update()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await SeedAsync(connection);
        await using var first = ScopedContext(state.Options, state.TenantId);
        await using var second = ScopedContext(state.Options, state.TenantId);
        var a = await first.TransportDrivers.SingleAsync(x => x.Id == state.DriverId);
        var b = await second.TransportDrivers.SingleAsync(x => x.Id == state.DriverId);
        Assert.Equal(8, a.RowVersion.Length);
        a.FullName = "Updated by first context";
        await first.SaveChangesAsync();
        b.FullName = "Stale second context";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }


    [Fact]
    public async Task Concurrent_vehicle_capacity_reduction_and_deactivation_reject_lost_updates()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await SeedAsync(connection);

        await using var first = ScopedContext(state.Options, state.TenantId);
        await using var second = ScopedContext(state.Options, state.TenantId);
        var capacityEditor = await first.Vehicles.SingleAsync(x => x.Id == state.Vehicle1Id);
        var statusEditor = await second.Vehicles.SingleAsync(x => x.Id == state.Vehicle1Id);
        Assert.Equal(8, capacityEditor.RowVersion.Length);
        Assert.Equal(capacityEditor.RowVersion, statusEditor.RowVersion);

        capacityEditor.Capacity = 10;
        statusEditor.IsActive = false;
        var committed = await Task.WhenAll(TryRowVersionCommitAsync(first), TryRowVersionCommitAsync(second));
        Assert.Equal(1, committed.Count(x => x));

        await using var verify = ScopedContext(state.Options, state.TenantId);
        var persisted = await verify.Vehicles.SingleAsync(x => x.Id == state.Vehicle1Id);
        Assert.True(
            (persisted.Capacity == 10 && persisted.IsActive) ||
            (persisted.Capacity == 20 && !persisted.IsActive),
            "A stale vehicle writer must not silently overwrite the competing capacity or activation change.");
    }

    [Fact]
    public async Task Concurrent_route_vehicle_replacement_and_period_change_reject_lost_updates()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await SeedAsync(connection);
        long linkId;
        await using (var seed = ScopedContext(state.Options, state.TenantId))
        {
            var link = new RouteVehicleAssignment
            {
                TenantId = state.TenantId, RouteId = state.Route1Id, VehicleId = state.Vehicle1Id,
                EffectiveFrom = new DateOnly(2027, 1, 10),
                EffectiveTo = new DateOnly(2027, 1, 20), IsCurrent = true
            };
            seed.RouteVehicleAssignments.Add(link);
            await seed.SaveChangesAsync();
            linkId = link.Id;
        }

        await using var first = ScopedContext(state.Options, state.TenantId);
        await using var second = ScopedContext(state.Options, state.TenantId);
        var vehicleEditor = await first.RouteVehicleAssignments.SingleAsync(x => x.Id == linkId);
        var periodEditor = await second.RouteVehicleAssignments.SingleAsync(x => x.Id == linkId);
        Assert.Equal(8, vehicleEditor.RowVersion.Length);
        Assert.Equal(vehicleEditor.RowVersion, periodEditor.RowVersion);

        vehicleEditor.VehicleId = state.Vehicle2Id;
        periodEditor.EffectiveTo = new DateOnly(2027, 1, 22);
        var committed = await Task.WhenAll(TryRowVersionCommitAsync(first), TryRowVersionCommitAsync(second));
        Assert.Equal(1, committed.Count(x => x));

        await using var verify = ScopedContext(state.Options, state.TenantId);
        var persisted = await verify.RouteVehicleAssignments.SingleAsync(x => x.Id == linkId);
        Assert.True(
            (persisted.VehicleId == state.Vehicle2Id && persisted.EffectiveTo == new DateOnly(2027, 1, 20)) ||
            (persisted.VehicleId == state.Vehicle1Id && persisted.EffectiveTo == new DateOnly(2027, 1, 22)),
            "A stale route-link writer must not combine incompatible changes from concurrent commands.");
    }

    private static async Task<bool> TryRowVersionCommitAsync(EduOSDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static async Task AssertOneCommitAsync(SeedState state, Action<EduOSDbContext, int> stage)
    {
        async Task<bool> WriteAsync(int contender)
        {
            await using var db = ScopedContext(state.Options, state.TenantId);
            stage(db, contender);
            await Task.Yield();
            try
            {
                await db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return false;
            }
        }

        var committed = await Task.WhenAll(WriteAsync(0), WriteAsync(1));
        Assert.Equal(1, committed.Count(x => x));
    }

    private static async Task<SeedState> SeedAsync(string connection)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = new EduOSDbContext(options);
        var tenant = new Tenant
        {
            Code = "CI-RACE-" + suffix, Name = "Transport concurrency CI",
            Email = "race-" + suffix + "@example.test", State = TenantState.Active
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        using (db.BeginSystemTenantScope(tenant.Id))
        {
            var campus = new Campus
            {
                TenantId = tenant.Id, Code = "RACE-" + suffix,
                Name = "Concurrency campus", IsActive = true
            };
            db.Campuses.Add(campus);
            await db.SaveChangesAsync();
            var route1 = new Route
            {
                TenantId = tenant.Id, CampusId = campus.Id, Name = "Route one",
                Code = "R1-" + suffix, IsActive = true
            };
            var route2 = new Route
            {
                TenantId = tenant.Id, CampusId = campus.Id, Name = "Route two",
                Code = "R2-" + suffix, IsActive = true
            };
            var vehicle1 = new Vehicle
            {
                TenantId = tenant.Id, CampusId = campus.Id,
                VehicleNumber = "CI-RACE-1-" + suffix, Capacity = 20, IsActive = true
            };
            var vehicle2 = new Vehicle
            {
                TenantId = tenant.Id, CampusId = campus.Id,
                VehicleNumber = "CI-RACE-2-" + suffix, Capacity = 20, IsActive = true
            };
            var driver = new TransportDriver
            {
                TenantId = tenant.Id, FullName = "Concurrent assignment driver",
                LicenseNumber = "CI-RACE-LIC-" + suffix,
                LicenseExpiresOn = new DateOnly(2028, 12, 31), IsActive = true
            };
            db.AddRange(route1, route2, vehicle1, vehicle2, driver);
            await db.SaveChangesAsync();
            return new SeedState(options, tenant.Id, campus.Id, route1.Id, route2.Id,
                vehicle1.Id, vehicle2.Id, driver.Id, suffix);
        }
    }

    private static EduOSDbContext ScopedContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record SeedState(DbContextOptions<EduOSDbContext> Options, long TenantId,
        long CampusId, long Route1Id, long Route2Id, long Vehicle1Id, long Vehicle2Id,
        long DriverId, string Suffix);
}
