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

public sealed class InventoryStockPositionServiceTests
{
    private static EduOSDbContext Context(long tenant)
    {
        var http = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin")], "Test"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("stock-position-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }
    private sealed class User(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Inventory Tester";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static InventoryMovementService Service(EduOSDbContext db, string role = "TenantAdmin") =>
        new(db, new User(101, role), TimeProvider.System, NullLogger<InventoryMovementService>.Instance);

    [Fact]
    public async Task Position_includes_zero_and_inactive_items_but_only_posted_costed_movements()
    {
        await using var db = Context(101);
        var store = new InventoryLocation { TenantId = 101, Code = "MAIN", Name = "Main" };
        var pen = new InventoryItem { TenantId = 101, Code = "PEN", Name = "Pen", ReorderLevel = 12m };
        var book = new InventoryItem { TenantId = 101, Code = "BOOK", Name = "Book", IsActive = false };
        var serviceItem = new InventoryItem { TenantId = 101, Code = "SERVICE", Name = "Service", IsStockTracked = false };
        db.InventoryLocations.Add(store); db.InventoryItems.AddRange(pen, book, serviceItem);
        await db.SaveChangesAsync();
        var receipt = new InventoryMovement { TenantId = 101, MovementNumber = "R1", MovementTypeCode = "Receipt",
            StateCode = "Posted", ToLocationId = store.Id, PostedAt = DateTime.UtcNow.AddMinutes(-5) };
        var issue = new InventoryMovement { TenantId = 101, MovementNumber = "I1", MovementTypeCode = "Issue",
            StateCode = "Posted", FromLocationId = store.Id, PostedAt = DateTime.UtcNow.AddMinutes(-4) };
        var reversal = new InventoryMovement { TenantId = 101, MovementNumber = "RV1", MovementTypeCode = "ReversalIssue",
            StateCode = "Posted", FromLocationId = store.Id, ReversalOfMovementId = issue.Id,
            PostedAt = DateTime.UtcNow.AddMinutes(-3) };
        var draft = new InventoryMovement { TenantId = 101, MovementNumber = "D1", MovementTypeCode = "Receipt",
            StateCode = "Draft", ToLocationId = store.Id };
        db.InventoryMovements.AddRange(receipt, issue, draft);
        await db.SaveChangesAsync();
        reversal.ReversalOfMovementId = issue.Id;
        db.InventoryMovements.Add(reversal);
        await db.SaveChangesAsync();
        db.InventoryMovementLines.AddRange(
            new InventoryMovementLine { TenantId = 101, InventoryMovementId = receipt.Id, InventoryItemId = pen.Id, Quantity = 10m, UnitCost = 2m },
            new InventoryMovementLine { TenantId = 101, InventoryMovementId = issue.Id, InventoryItemId = pen.Id, Quantity = 4m, UnitCost = 2m },
            new InventoryMovementLine { TenantId = 101, InventoryMovementId = reversal.Id, InventoryItemId = pen.Id, Quantity = 4m, UnitCost = 2m },
            new InventoryMovementLine { TenantId = 101, InventoryMovementId = draft.Id, InventoryItemId = pen.Id, Quantity = 99m, UnitCost = 2m });
        await db.SaveChangesAsync();
        var result = await Service(db).GetStockPositionAsync(store.Id, null, null, true, 1, 50);
        result.Success.Should().BeTrue();
        result.Data!.CatalogItemCount.Should().Be(2);
        result.Data.Items.Should().HaveCount(2);
        var stock = result.Data.Items.Single(x => x.ItemCode == "PEN");
        stock.QuantityOnHand.Should().Be(10m);
        stock.RecordedCostBalance.Should().Be(20m);
        stock.IsBelowReorder.Should().BeTrue();
        result.Data.Items.Single(x => x.ItemCode == "BOOK").IsActive.Should().BeFalse();
        (await Service(db).GetStockPositionAsync(store.Id, null, null, false, 1, 50))
            .Data!.Items.Should().ContainSingle(x => x.ItemCode == "PEN");
    }

    [Fact]
    public async Task Position_enforces_campus_grants_tenant_scope_and_roles()
    {
        await using var db = Context(101);
        var a = new Campus { TenantId = 101, Code = "A", Name = "Campus A" };
        var b = new Campus { TenantId = 101, Code = "B", Name = "Campus B" };
        db.Campuses.AddRange(a, b); await db.SaveChangesAsync();
        var own = new InventoryLocation { TenantId = 101, CampusId = a.Id, Code = "A1", Name = "Store A" };
        var blocked = new InventoryLocation { TenantId = 101, CampusId = b.Id, Code = "B1", Name = "Store B" };
        var foreign = new InventoryLocation { TenantId = 102, Code = "X1", Name = "Foreign" };
        db.InventoryLocations.AddRange(own, blocked, foreign);
        db.UserCampusAccesses.Add(new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = a.Id, IsActive = true });
        await db.SaveChangesAsync();
        (await Service(db, "StoreKeeper").GetStockPositionAsync(own.Id, null, null, true, 1, 25))
            .StatusCode.Should().Be(200);
        (await Service(db, "StoreKeeper").GetStockPositionAsync(blocked.Id, null, null, true, 1, 25))
            .StatusCode.Should().Be(403);
        (await Service(db).GetStockPositionAsync(foreign.Id, null, null, true, 1, 25))
            .StatusCode.Should().Be(403);
        (await Service(db, "Student").GetStockPositionAsync(own.Id, null, null, true, 1, 25))
            .StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Position_rejects_negative_posted_lots_and_invalid_snapshot()
    {
        await using var db = Context(101);
        var store = new InventoryLocation { TenantId = 101, Code = "S", Name = "Store" };
        var item = new InventoryItem { TenantId = 101, Code = "P", Name = "Paper" };
        db.InventoryLocations.Add(store); db.InventoryItems.Add(item); await db.SaveChangesAsync();
        var issue = new InventoryMovement { TenantId = 101, MovementNumber = "I", MovementTypeCode = "Issue",
            StateCode = "Posted", FromLocationId = store.Id, PostedAt = DateTime.UtcNow.AddMinutes(-1) };
        db.InventoryMovements.Add(issue); await db.SaveChangesAsync();
        db.InventoryMovementLines.Add(new InventoryMovementLine {
            TenantId = 101, InventoryMovementId = issue.Id, InventoryItemId = item.Id, Quantity = 1m, UnitCost = 1m });
        await db.SaveChangesAsync();
        (await Service(db).GetStockPositionAsync(store.Id, null, null, true, 1, 25))
            .StatusCode.Should().Be(409);
        (await Service(db).GetStockPositionAsync(store.Id, DateTime.UtcNow.AddDays(1), null, true, 1, 25))
            .StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Position_search_and_pagination_preserve_order_and_as_of_posting()
    {
        await using var db = Context(101);
        var store = new InventoryLocation { TenantId = 101, Code = "S", Name = "Store" };
        var a = new InventoryItem { TenantId = 101, Code = "A", Name = "Paper A" };
        var b = new InventoryItem { TenantId = 101, Code = "B", Name = "Paper B" };
        db.InventoryLocations.Add(store); db.InventoryItems.AddRange(a, b); await db.SaveChangesAsync();
        var receipt = new InventoryMovement { TenantId = 101, MovementNumber = "R", MovementTypeCode = "Receipt",
            StateCode = "Posted", ToLocationId = store.Id, PostedAt = DateTime.UtcNow.AddMinutes(-1) };
        db.InventoryMovements.Add(receipt); await db.SaveChangesAsync();
        db.InventoryMovementLines.Add(new InventoryMovementLine {
            TenantId = 101, InventoryMovementId = receipt.Id, InventoryItemId = a.Id, Quantity = 5m, UnitCost = 2m });
        await db.SaveChangesAsync();
        var snapshot = DateTime.UtcNow.AddMinutes(-2);
        var first = await Service(db).GetStockPositionAsync(store.Id, snapshot, "Paper", true, 1, 1);
        first.Success.Should().BeTrue();
        first.Data!.TotalCount.Should().Be(2);
        first.Data.Items.Single().ItemCode.Should().Be("A");
        first.Data.Items.Single().QuantityOnHand.Should().Be(0m);
        var second = await Service(db).GetStockPositionAsync(store.Id, snapshot, "Paper", true, 2, 1);
        second.Data!.Items.Single().ItemCode.Should().Be("B");
    }
}
