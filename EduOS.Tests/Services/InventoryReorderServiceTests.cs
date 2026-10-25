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

public sealed class InventoryReorderServiceTests
{
    private static EduOSDbContext Context(string database, long tenant)
    {
        var http = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin")], "Test"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(database).Options, new HttpContextAccessor { HttpContext = http });
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
    private static InventoryMovementService Service(EduOSDbContext db, long tenant, string role = "TenantAdmin") =>
        new(db, new User(tenant, role), TimeProvider.System, NullLogger<InventoryMovementService>.Instance);

    [Fact]
    public async Task Reorder_report_includes_zero_stock_and_posted_reversal_aware_balances()
    {
        await using var db = Context("reorder-" + Guid.NewGuid().ToString("N"), 101);
        var store = new InventoryLocation { TenantId = 101, Code = "MAIN", Name = "Main Store" };
        var pen = new InventoryItem { TenantId = 101, Code = "PEN", Name = "Pen", ReorderLevel = 10m };
        var book = new InventoryItem { TenantId = 101, Code = "BOOK", Name = "Book", ReorderLevel = 5m };
        var untracked = new InventoryItem { TenantId = 101, Code = "UNTRACKED", Name = "Untracked", IsStockTracked = false, ReorderLevel = 99m };
        db.InventoryLocations.Add(store); db.InventoryItems.AddRange(pen, book, untracked);
        await db.SaveChangesAsync();
        var receipt = new InventoryMovement { TenantId = 101, MovementNumber = "R1",
            MovementTypeCode = "Receipt", StateCode = "Posted", ToLocationId = store.Id,
            PostedAt = DateTime.UtcNow.AddMinutes(-2) };
        var issue = new InventoryMovement { TenantId = 101, MovementNumber = "I1",
            MovementTypeCode = "Issue", StateCode = "Posted", FromLocationId = store.Id,
            PostedAt = DateTime.UtcNow.AddMinutes(-1) };
        var draft = new InventoryMovement { TenantId = 101, MovementNumber = "D1",
            MovementTypeCode = "Receipt", StateCode = "Draft", ToLocationId = store.Id };
        db.InventoryMovements.AddRange(receipt, issue, draft);
        await db.SaveChangesAsync();
        db.InventoryMovementLines.AddRange(
            new InventoryMovementLine { TenantId = 101, InventoryMovementId = receipt.Id, InventoryItemId = pen.Id, Quantity = 6m, UnitCost = 2m },
            new InventoryMovementLine { TenantId = 101, InventoryMovementId = issue.Id, InventoryItemId = pen.Id, Quantity = 2m, UnitCost = 2m },
            new InventoryMovementLine { TenantId = 101, InventoryMovementId = draft.Id, InventoryItemId = book.Id, Quantity = 99m, UnitCost = 2m });
        await db.SaveChangesAsync();
        var result = await Service(db, 101).GetReorderAlertsAsync(store.Id);
        result.Success.Should().BeTrue();
        result.Data!.MonitoredItemCount.Should().Be(2);
        result.Data.Alerts.Should().HaveCount(2);
        result.Data.Alerts.Single(x => x.ItemCode == "PEN").QuantityOnHand.Should().Be(4m);
        result.Data.Alerts.Single(x => x.ItemCode == "PEN").Shortfall.Should().Be(6m);
        result.Data.Alerts.Single(x => x.ItemCode == "BOOK").QuantityOnHand.Should().Be(0m);
    }

    [Fact]
    public async Task Reorder_report_enforces_campus_grants_and_role()
    {
        await using var db = Context("reorder-campus-" + Guid.NewGuid().ToString("N"), 101);
        var a = new Campus { TenantId = 101, Code = "A", Name = "Campus A" };
        var b = new Campus { TenantId = 101, Code = "B", Name = "Campus B" };
        db.Campuses.AddRange(a, b); await db.SaveChangesAsync();
        var own = new InventoryLocation { TenantId = 101, CampusId = a.Id, Code = "A1", Name = "Store A" };
        var blocked = new InventoryLocation { TenantId = 101, CampusId = b.Id, Code = "B1", Name = "Store B" };
        db.InventoryLocations.AddRange(own, blocked);
        db.UserCampusAccesses.Add(new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = a.Id, IsActive = true });
        await db.SaveChangesAsync();
        (await Service(db, 101, "StoreKeeper").GetReorderAlertsAsync(own.Id)).StatusCode.Should().Be(200);
        (await Service(db, 101, "StoreKeeper").GetReorderAlertsAsync(blocked.Id)).StatusCode.Should().Be(403);
        (await Service(db, 101, "Student").GetReorderAlertsAsync(own.Id)).StatusCode.Should().Be(403);
    }
}
