using EduOS.Core.DTOs.Inventory;
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

public sealed class InventoryCatalogServiceTests
{
    private static DbContextOptions<EduOSDbContext> Options() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase("inventory-catalog-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim("TenantId", tenant.ToString()),
            new Claim(ClaimTypes.Role, "TenantAdmin")], "TestAuthentication")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static InventoryCatalogService Service(EduOSDbContext db, long tenant, string role = "TenantAdmin") =>
        new(db, new CurrentUser(tenant, role), TimeProvider.System, NullLogger<InventoryCatalogService>.Instance);

    private sealed class CurrentUser(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Catalog Tester";
        public string? Email => "test@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    [Fact]
    public async Task Create_is_idempotent_and_duplicate_code_with_different_details_conflicts()
    {
        await using var db = Context(Options(), 101);
        var service = Service(db, 101);
        var request = new SaveInventoryItemRequestDto { Code = " pen ", Name = "Blue Pen", UnitCode = "Each", ReorderLevel = 10 };
        var first = await service.SaveItemAsync(null, request);
        var retry = await service.SaveItemAsync(null, request);
        var conflict = await service.SaveItemAsync(null, new SaveInventoryItemRequestDto { Code = "PEN", Name = "Red Pen" });
        first.StatusCode.Should().Be(201);
        first.Data!.Code.Should().Be("PEN");
        retry.Success.Should().BeTrue();
        retry.Data!.Id.Should().Be(first.Data.Id);
        conflict.StatusCode.Should().Be(409);
        (await db.InventoryItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Tenant_isolation_applies_to_reads_and_mutations()
    {
        var options = Options();
        long otherId;
        await using (var foreign = Context(options, 202))
        {
            var created = await Service(foreign, 202).SaveItemAsync(null, new SaveInventoryItemRequestDto { Code = "BOOK", Name = "Foreign book" });
            otherId = created.Data!.Id;
        }
        await using var db = Context(options, 101);
        var service = Service(db, 101);
        var list = await service.GetItemsAsync(1, 20, null);
        list.Data!.TotalCount.Should().Be(0);
        var edit = await service.SaveItemAsync(otherId, new SaveInventoryItemRequestDto { Code = "BOOK", Name = "Altered", RowVersion = "AQ==" });
        edit.StatusCode.Should().Be(404);
        var own = await service.SaveItemAsync(null, new SaveInventoryItemRequestDto { Code = "BOOK", Name = "Own book" });
        own.StatusCode.Should().Be(201);
        (await db.InventoryItems.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Stale_version_and_unprivileged_writes_are_rejected()
    {
        await using var db = Context(Options(), 101);
        var service = Service(db, 101);
        var created = await service.SaveItemAsync(null, new SaveInventoryItemRequestDto { Code = "CHAIR", Name = "Chair" });
        var entity = await db.InventoryItems.SingleAsync(x => x.Id == created.Data!.Id);
        entity.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await db.SaveChangesAsync();
        var stale = await service.SaveItemAsync(entity.Id, new SaveInventoryItemRequestDto { Code = "CHAIR", Name = "Changed", RowVersion = "CQkJCQkJCQk=" });
        stale.StatusCode.Should().Be(409);
        entity.Name.Should().Be("Chair");
        var forbidden = await Service(db, 101, "Student").SaveItemAsync(null, new SaveInventoryItemRequestDto { Code = "NEW", Name = "New" });
        forbidden.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Locations_reject_cross_tenant_campus_and_allow_same_tenant_retries()
    {
        var options = Options();
        long foreignId;
        await using (var other = Context(options, 202))
        {
            var campus = new Campus { TenantId = 202, Code = "F", Name = "Foreign Campus" };
            other.Campuses.Add(campus); await other.SaveChangesAsync(); foreignId = campus.Id;
        }
        await using var db = Context(options, 101);
        var service = Service(db, 101);
        var denied = await service.SaveLocationAsync(null, new SaveInventoryLocationRequestDto { Code = "S1", Name = "Store", CampusId = foreignId });
        denied.StatusCode.Should().Be(404);
        var request = new SaveInventoryLocationRequestDto { Code = "S1", Name = "Store", IsActive = true };
        var first = await service.SaveLocationAsync(null, request);
        var retry = await service.SaveLocationAsync(null, request);
        first.StatusCode.Should().Be(201);
        retry.Data!.Id.Should().Be(first.Data!.Id);
        (await db.InventoryLocations.CountAsync()).Should().Be(1);
    }
}
