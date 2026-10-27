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

public sealed class InventoryItemHistoryServiceTests
{
    private static EduOSDbContext Context(long tenant = 101)
    {
        var http = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin")], "Test"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("item-history-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    private sealed class User(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "History Tester";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static InventoryMovementService Service(EduOSDbContext db, string role = "TenantAdmin", long tenant = 101) =>
        new(db, new User(tenant, role), TimeProvider.System, NullLogger<InventoryMovementService>.Instance);

    private static readonly DateTime Day1 = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day3 = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day4 = new(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Item_history_reconciles_opening_period_and_reversals_without_drafts()
    {
        await using var db = Context();
        var a = new InventoryLocation { TenantId = 101, Code = "A", Name = "Main" };
        var b = new InventoryLocation { TenantId = 101, Code = "B", Name = "Branch" };
        var item = new InventoryItem { TenantId = 101, Code = "PEN", Name = "Pen" };
        db.InventoryLocations.AddRange(a, b); db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        var receipt = new InventoryMovement { TenantId = 101, MovementNumber = "R", MovementTypeCode = "Receipt",
            StateCode = "Posted", ToLocationId = a.Id, PostedAt = Day1, SourceType = "ManualReceipt" };
        var issue = new InventoryMovement { TenantId = 101, MovementNumber = "I", MovementTypeCode = "Issue",
            StateCode = "Posted", FromLocationId = a.Id, PostedAt = Day2, SourceType = "ManualIssue" };
        var transfer = new InventoryMovement { TenantId = 101, MovementNumber = "T", MovementTypeCode = "Transfer",
            StateCode = "Posted", FromLocationId = a.Id, ToLocationId = b.Id, PostedAt = Day3, SourceType = "ManualTransfer" };
        var draft = new InventoryMovement { TenantId = 101, MovementNumber = "D", MovementTypeCode = "Receipt",
            StateCode = "Draft", ToLocationId = a.Id, PostedAt = Day4 };
        db.InventoryMovements.AddRange(receipt, issue, transfer, draft);
        await db.SaveChangesAsync();
        var reversal = new InventoryMovement { TenantId = 101, MovementNumber = "RT", MovementTypeCode = "ReversalTransfer",
            StateCode = "Posted", FromLocationId = a.Id, ToLocationId = b.Id,
            PostedAt = Day4, ReversalOfMovementId = transfer.Id,
            SourceType = "MovementReversal", SourceId = transfer.Id };
        db.InventoryMovements.Add(reversal);
        await db.SaveChangesAsync();
        foreach (var (movement, quantity) in new[] { (receipt, 20m), (issue, 4m), (transfer, 6m),
            (reversal, 6m), (draft, 99m) })
            db.InventoryMovementLines.Add(new InventoryMovementLine {
                TenantId = 101, InventoryMovementId = movement.Id, InventoryItemId = item.Id,
                LineNo = 1, Quantity = quantity, UnitCost = 5m, LotOrBatchNo = "L1" });
        await db.SaveChangesAsync();

        var result = await Service(db).GetItemHistoryAsync(item.Id, a.Id, Day2, Day4, "L1", 1, 2);
        result.StatusCode.Should().Be(200);
        result.Data!.OpeningQuantity.Should().Be(20m);
        result.Data.OpeningRecordedCost.Should().Be(100m);
        result.Data.QuantityIn.Should().Be(6m);
        result.Data.QuantityOut.Should().Be(10m);
        result.Data.ClosingQuantity.Should().Be(16m);
        result.Data.ClosingRecordedCost.Should().Be(80m);
        result.Data.TotalCount.Should().Be(3);
        result.Data.Items.Should().HaveCount(2);
        result.Data.Items.Select(x => x.MovementNumber).Should().Equal("I", "T");
        var second = await Service(db).GetItemHistoryAsync(item.Id, a.Id, Day2, Day4, "L1", 2, 2);
        second.Data!.Items.Should().ContainSingle(x => x.MovementNumber == "RT" &&
            x.IsReversal && x.SourceId == transfer.Id && x.QuantityIn == 6m);

        var destination = await Service(db).GetItemHistoryAsync(item.Id, b.Id, null, Day4, null, 1, 25);
        destination.Data!.QuantityIn.Should().Be(6m);
        destination.Data.QuantityOut.Should().Be(6m);
        destination.Data.ClosingQuantity.Should().Be(0m);
        destination.Data.ClosingRecordedCost.Should().Be(0m);
    }

    [Fact]
    public async Task History_rejects_soft_deleted_posted_lines_but_not_drafts()
    {
        await using var db = Context();
        var store = new InventoryLocation { TenantId = 101, Code = "A", Name = "Main" };
        var item = new InventoryItem { TenantId = 101, Code = "P", Name = "Paper" };
        db.InventoryLocations.Add(store); db.InventoryItems.Add(item); await db.SaveChangesAsync();
        var posted = new InventoryMovement { TenantId = 101, MovementNumber = "P", MovementTypeCode = "Receipt",
            StateCode = "Posted", ToLocationId = store.Id, PostedAt = Day1 };
        var draft = new InventoryMovement { TenantId = 101, MovementNumber = "D", MovementTypeCode = "Receipt",
            StateCode = "Draft", ToLocationId = store.Id };
        db.InventoryMovements.AddRange(posted, draft); await db.SaveChangesAsync();
        var line = new InventoryMovementLine { TenantId = 101, InventoryMovementId = posted.Id,
            InventoryItemId = item.Id, Quantity = 3m, UnitCost = 2m, IsDeleted = true };
        db.InventoryMovementLines.Add(line);
        db.InventoryMovementLines.Add(new InventoryMovementLine { TenantId = 101,
            InventoryMovementId = draft.Id, InventoryItemId = item.Id, Quantity = 5m, IsDeleted = true });
        await db.SaveChangesAsync();
        (await Service(db).GetItemHistoryAsync(item.Id, store.Id, null, null, null, 1, 25))
            .StatusCode.Should().Be(409);
        // Do not mutate a posted line to repair the fixture: DbContext correctly
        // enforces posted stock immutability. A different item with only a draft
        // tombstone must remain readable.
        var cleanItem = new InventoryItem { TenantId = 101, Code = "C", Name = "Clean paper" };
        db.InventoryItems.Add(cleanItem);
        await db.SaveChangesAsync();
        db.InventoryMovementLines.Add(new InventoryMovementLine {
            TenantId = 101, InventoryMovementId = posted.Id, InventoryItemId = cleanItem.Id,
            Quantity = 3m, UnitCost = 2m });
        db.InventoryMovementLines.Add(new InventoryMovementLine {
            TenantId = 101, InventoryMovementId = draft.Id, InventoryItemId = cleanItem.Id,
            Quantity = 5m, IsDeleted = true });
        await db.SaveChangesAsync();
        var result = await Service(db).GetItemHistoryAsync(cleanItem.Id, store.Id, null, null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.ClosingQuantity.Should().Be(3m);
    }

    [Fact]
    public async Task History_enforces_tenant_campus_roles_and_filter_validation()
    {
        await using var db = Context();
        var a = new Campus { TenantId = 101, Code = "A", Name = "Campus A" };
        var b = new Campus { TenantId = 101, Code = "B", Name = "Campus B" };
        db.Campuses.AddRange(a, b); await db.SaveChangesAsync();
        var allowed = new InventoryLocation { TenantId = 101, CampusId = a.Id, Code = "A1", Name = "Store A" };
        var denied = new InventoryLocation { TenantId = 101, CampusId = b.Id, Code = "B1", Name = "Store B" };
        var item = new InventoryItem { TenantId = 101, Code = "P", Name = "Paper" };
        db.InventoryLocations.AddRange(allowed, denied); db.InventoryItems.Add(item);
        db.UserCampusAccesses.Add(new UserCampusAccess { TenantId = 101, UserId = 7,
            CampusId = a.Id, IsActive = true });
        await db.SaveChangesAsync();
        (await Service(db, "StoreKeeper").GetItemHistoryAsync(item.Id, allowed.Id, null, null, null, 1, 25))
            .StatusCode.Should().Be(200);
        (await Service(db, "StoreKeeper").GetItemHistoryAsync(item.Id, denied.Id, null, null, null, 1, 25))
            .StatusCode.Should().Be(403);
        (await Service(db, "TenantAdmin", 202).GetItemHistoryAsync(item.Id, allowed.Id, null, null, null, 1, 25))
            .StatusCode.Should().Be(403);
        (await Service(db, "Student").GetItemHistoryAsync(item.Id, allowed.Id, null, null, null, 1, 25))
            .StatusCode.Should().Be(403);
        (await Service(db).GetItemHistoryAsync(item.Id, allowed.Id, Day4, Day1, null, 1, 25))
            .StatusCode.Should().Be(400);
        (await Service(db).GetItemHistoryAsync(item.Id, allowed.Id, null, null, null, 1, 101))
            .StatusCode.Should().Be(400);
    }
}
