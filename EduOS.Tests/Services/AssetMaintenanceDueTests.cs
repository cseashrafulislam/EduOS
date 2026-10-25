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
public sealed class AssetMaintenanceDueTests
{
    private static EduOSDbContext Context(string name, long tenant)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin")], "Test")) };
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
    public async Task Latest_schedule_supersedes_old_due_and_inactive_assets_are_excluded()
    {
        await using var db = Context("asset-due-" + Guid.NewGuid().ToString("N"), 101);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var due = new Asset { TenantId = 101, AssetTag = "A", Name = "Generator" };
        var cleared = new Asset { TenantId = 101, AssetTag = "B", Name = "Computer" };
        var future = new Asset { TenantId = 101, AssetTag = "C", Name = "Pump" };
        var inactive = new Asset { TenantId = 101, AssetTag = "D", Name = "Old", IsActive = false };
        db.Assets.AddRange(due, cleared, future, inactive);
        await db.SaveChangesAsync();
        db.AssetMaintenances.AddRange(
            new AssetMaintenance { TenantId = 101, AssetId = due.Id, MaintenanceDate = today.AddDays(-20),
                NextMaintenanceDate = today.AddDays(-5), WorkDescription = "Service", Cost = 12.25m },
            new AssetMaintenance { TenantId = 101, AssetId = cleared.Id, MaintenanceDate = today.AddDays(-30),
                NextMaintenanceDate = today.AddDays(-10), WorkDescription = "Old service" },
            new AssetMaintenance { TenantId = 101, AssetId = cleared.Id, MaintenanceDate = today.AddDays(-2),
                WorkDescription = "New service without schedule" },
            new AssetMaintenance { TenantId = 101, AssetId = future.Id, MaintenanceDate = today.AddDays(-1),
                NextMaintenanceDate = today.AddDays(10), WorkDescription = "Recent" },
            new AssetMaintenance { TenantId = 101, AssetId = inactive.Id, MaintenanceDate = today.AddDays(-1),
                NextMaintenanceDate = today, WorkDescription = "Inactive" });
        await db.SaveChangesAsync();
        var result = await Service(db, 101).GetMaintenanceDueAsync(today, 1, 25);
        result.Success.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items[0].AssetReference.Should().Be(due.PublicId);
        result.Data.Items[0].DaysOverdue.Should().Be(5);
        (await Service(db, 101).GetMaintenanceDueAsync(today.AddDays(20), 1, 25))
            .Data!.TotalCount.Should().Be(2);
    }
    [Fact]
    public async Task Due_register_enforces_campus_and_role_scope()
    {
        await using var db = Context("asset-due-campus-" + Guid.NewGuid().ToString("N"), 101);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var allowed = new Campus { TenantId = 101, Code = "A", Name = "Allowed" };
        var denied = new Campus { TenantId = 101, Code = "B", Name = "Denied" };
        db.Campuses.AddRange(allowed, denied);
        await db.SaveChangesAsync();
        var own = new Asset { TenantId = 101, CampusId = allowed.Id, AssetTag = "A", Name = "Own" };
        var other = new Asset { TenantId = 101, CampusId = denied.Id, AssetTag = "B", Name = "Other" };
        var shared = new Asset { TenantId = 101, AssetTag = "S", Name = "Shared" };
        db.Assets.AddRange(own, other, shared);
        db.UserCampusAccesses.Add(new UserCampusAccess
            { TenantId = 101, UserId = 7, CampusId = allowed.Id, IsActive = true });
        await db.SaveChangesAsync();
        foreach (var asset in new[] { own, other, shared })
            db.AssetMaintenances.Add(new AssetMaintenance
                { TenantId = 101, AssetId = asset.Id, MaintenanceDate = today.AddDays(-5),
                  NextMaintenanceDate = today, WorkDescription = "Inspection" });
        await db.SaveChangesAsync();
        (await Service(db, 101, "InventoryManager").GetMaintenanceDueAsync(today, 1, 25))
            .Data!.Items.Select(x => x.AssetReference).Should().Equal(own.PublicId);
        (await Service(db, 101).GetMaintenanceDueAsync(today, 1, 25)).Data!.TotalCount.Should().Be(3);
        (await Service(db, 101, "Student").GetMaintenanceDueAsync(today, 1, 25)).StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Due_register_rejects_unbounded_queries()
    {
        await using var db = Context("asset-due-validation-" + Guid.NewGuid().ToString("N"), 101);
        var service = Service(db, 101);
        (await service.GetMaintenanceDueAsync(null, 0, 25)).StatusCode.Should().Be(400);
        (await service.GetMaintenanceDueAsync(null, 1, 101)).StatusCode.Should().Be(400);
        (await service.GetMaintenanceDueAsync(null, int.MaxValue, 100)).StatusCode.Should().Be(400);
        (await service.GetMaintenanceDueAsync(new DateOnly(1999, 1, 1), 1, 25)).StatusCode.Should().Be(400);
        (await service.GetMaintenanceDueAsync(null, 1, 25)).Data!.TotalCount.Should().Be(0);
    }
}
