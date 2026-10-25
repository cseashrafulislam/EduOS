using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssetMaintenanceHistoryTests
{
    private static EduOSDbContext Context(string name, long tenant)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin")], "Test"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name).Options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class User(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Asset Tester";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static AssetLifecycleService Service(EduOSDbContext db, long tenant, string role = "TenantAdmin") =>
        new(db, new User(tenant, role), TimeProvider.System, NullLogger<AssetLifecycleService>.Instance);

    [Fact]
    public async Task Maintenance_history_is_read_only_paged_ordered_and_excludes_other_assets()
    {
        await using var db = Context("asset-maintenance-" + Guid.NewGuid().ToString("N"), 101);
        var asset = new Asset { TenantId = 101, AssetTag = "A-1", Name = "Generator" };
        var other = new Asset { TenantId = 101, AssetTag = "A-2", Name = "Computer" };
        db.Assets.AddRange(asset, other);
        await db.SaveChangesAsync();
        db.AssetMaintenances.AddRange(
            new AssetMaintenance { TenantId = 101, AssetId = asset.Id, MaintenanceDate = new DateOnly(2026, 1, 1),
                WorkDescription = "Oil", Cost = 12.50m, Remarks = "RequestId:secret;" },
            new AssetMaintenance { TenantId = 101, AssetId = asset.Id, MaintenanceDate = new DateOnly(2026, 3, 1),
                WorkDescription = "Filter", Cost = 15m, ServiceProvider = "Workshop", NextMaintenanceDate = new DateOnly(2026, 6, 1) },
            new AssetMaintenance { TenantId = 101, AssetId = asset.Id, MaintenanceDate = new DateOnly(2026, 2, 1),
                WorkDescription = "Deleted", Cost = 99m, IsDeleted = true },
            new AssetMaintenance { TenantId = 101, AssetId = other.Id, MaintenanceDate = new DateOnly(2026, 4, 1),
                WorkDescription = "Other", Cost = 999m });
        await db.SaveChangesAsync();

        var first = await Service(db, 101).GetMaintenanceHistoryAsync(asset.PublicId, 1, 1);
        first.Success.Should().BeTrue();
        first.Data!.TotalCount.Should().Be(2);
        first.Data.Items.Should().ContainSingle();
        first.Data.Items[0].WorkDescription.Should().Be("Filter");
        first.Data.Items[0].ServiceProvider.Should().Be("Workshop");
        first.Data.Items[0].NextMaintenanceDate.Should().Be(new DateOnly(2026, 6, 1));
        var second = await Service(db, 101).GetMaintenanceHistoryAsync(asset.PublicId, 2, 1);
        second.Data!.Items.Should().ContainSingle();
        second.Data.Items[0].WorkDescription.Should().Be("Oil");
        typeof(EduOS.Core.DTOs.Inventory.AssetMaintenanceDto).GetProperty("Remarks").Should().BeNull();
        (await Service(db, 101).GetMaintenanceHistoryAsync(asset.PublicId, 0, 10)).StatusCode.Should().Be(400);
        (await Service(db, 101).GetMaintenanceHistoryAsync(asset.PublicId, 1, 101)).StatusCode.Should().Be(400);
        (await Service(db, 101).GetMaintenanceHistoryAsync(Guid.Empty, 1, 10)).StatusCode.Should().Be(400);
        (await Service(db, 101).GetMaintenanceHistoryAsync(asset.PublicId, int.MaxValue, 100)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Maintenance_history_enforces_campus_and_role_authorization()
    {
        await using var db = Context("asset-maintenance-campus-" + Guid.NewGuid().ToString("N"), 101);
        var ownCampus = new Campus { TenantId = 101, Code = "A", Name = "Campus A" };
        var blockedCampus = new Campus { TenantId = 101, Code = "B", Name = "Campus B" };
        db.Campuses.AddRange(ownCampus, blockedCampus);
        await db.SaveChangesAsync();
        var own = new Asset { TenantId = 101, AssetTag = "A", Name = "Own", CampusId = ownCampus.Id };
        var blocked = new Asset { TenantId = 101, AssetTag = "B", Name = "Blocked", CampusId = blockedCampus.Id };
        var shared = new Asset { TenantId = 101, AssetTag = "S", Name = "Shared" };
        db.Assets.AddRange(own, blocked, shared);
        db.UserCampusAccesses.Add(new UserCampusAccess
            { TenantId = 101, UserId = 7, CampusId = ownCampus.Id, IsActive = true });
        await db.SaveChangesAsync();
        db.AssetMaintenances.Add(new AssetMaintenance
            { TenantId = 101, AssetId = own.Id, MaintenanceDate = new DateOnly(2026, 1, 1),
              WorkDescription = "Inspection", Cost = 0m });
        await db.SaveChangesAsync();
        var manager = Service(db, 101, "InventoryManager");
        (await manager.GetMaintenanceHistoryAsync(own.PublicId, 1, 10)).Data!.TotalCount.Should().Be(1);
        (await manager.GetMaintenanceHistoryAsync(blocked.PublicId, 1, 10)).StatusCode.Should().Be(404);
        (await manager.GetMaintenanceHistoryAsync(shared.PublicId, 1, 10)).StatusCode.Should().Be(404);
        (await Service(db, 101, "Student").GetMaintenanceHistoryAsync(own.PublicId, 1, 10)).StatusCode.Should().Be(403);
        (await Service(db, 101).GetMaintenanceHistoryAsync(shared.PublicId, 1, 10)).StatusCode.Should().Be(200);
    }
}
