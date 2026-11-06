using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Enterprise;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class V3EnterpriseLedgerTraceTests
{
    private sealed class Actor(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 1;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static EduOSDbContext Open(long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("v3-trace-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    [Fact]
    public async Task Trace_requires_tenant_admin_and_explicit_balanced_journal_link()
    {
        await using var db = Open(101);
        var m = new InventoryMovement {
            TenantId = 101, MovementNumber = "R1", MovementTypeCode = "Receipt",
            StateCode = "Posted", PostedAt = DateTime.UtcNow, SourceType = "PurchaseReceipt"
        };
        db.InventoryMovements.Add(m);
        await db.SaveChangesAsync();
        db.InventoryMovementLines.Add(new InventoryMovementLine {
            TenantId = 101, InventoryMovementId = m.Id, InventoryItemId = 12,
            LineNo = 1, Quantity = 3m, UnitCost = 20m
        });
        var j = new Journal {
            TenantId = 101, JournalNumber = "J1", SourceType = "InventoryMovement",
            SourceId = m.Id, State = JournalState.Posted
        };
        db.Journals.Add(j);
        await db.SaveChangesAsync();
        db.JournalLines.AddRange(
            new JournalLine { TenantId = 101, JournalId = j.Id, LineNo = 1, DebitAmount = 60m },
            new JournalLine { TenantId = 101, JournalId = j.Id, LineNo = 2, CreditAmount = 60m });
        await db.SaveChangesAsync();
        var service = new V3EnterpriseLedgerTraceService(db, new Actor(101, "TenantAdmin"));
        var ok = await service.GetAsync(m.PublicId);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal(60m, ok.Data!.RecordedMovementValue);
        Assert.Equal("ExplicitBalancedReference", ok.Data.FinanceEvidenceStatus);
        Assert.Single(ok.Data.Journals);
        Assert.True(ok.Data.Journals[0].Balanced);
        Assert.Equal(403, (await new V3EnterpriseLedgerTraceService(
            db, new Actor(101, "Student")).GetAsync(m.PublicId)).StatusCode);
        Assert.Equal(403, (await new V3EnterpriseLedgerTraceService(
            db, new Actor(999, "TenantAdmin")).GetAsync(m.PublicId)).StatusCode);
        Assert.Equal(404, (await service.GetAsync(Guid.NewGuid())).StatusCode);
        // Posted V1 Finance evidence is immutable; the trace never edits it.
        db.JournalLines.First().CreditAmount = 1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Reconciliation_register_denies_cross_tenant_context_and_unbounded_inputs()
    {
        await using var db = Open(101);
        var denied = await new V3EnterpriseReconciliationRegisterService(
            db, new Actor(101, "Student")).GetAsync("stock",
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(403, denied.StatusCode);
        var mismatch = await new V3EnterpriseReconciliationRegisterService(
            db, new Actor(102, "TenantAdmin")).GetAsync("stock",
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(403, mismatch.StatusCode);
        var service = new V3EnterpriseReconciliationRegisterService(db, new Actor(101, "TenantAdmin"));
        Assert.Equal(400, (await service.GetAsync("facility",
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))).StatusCode);
        Assert.Equal(400, (await service.GetAsync("stock",
            new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 1))).StatusCode);
        Assert.Equal(400, (await service.GetAsync("asset",
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), page: 0)).StatusCode);
    }

    [Fact]
    public async Task Stock_register_finds_GL_orphans_without_inventing_campus_or_changing_stock()
    {
        await using var db = Open(101);
        db.InventoryMovements.Add(new InventoryMovement {
            Id = 71, TenantId = 101, MovementNumber = "STOCK-71", MovementTypeCode = "Receipt",
            StateCode = "Posted", MovementAt = new DateTime(2026, 9, 30, 10, 0, 0)
        });
        db.Journals.AddRange(
            new Journal { TenantId = 101, JournalNumber = "J-VALID", SourceType = "InventoryMovement",
                SourceId = 71, State = JournalState.Posted, JournalDate = new DateOnly(2026, 10, 5) },
            new Journal { TenantId = 101, JournalNumber = "J-ORPHAN", SourceType = "InventoryMovement",
                SourceId = 99, State = JournalState.Posted, JournalDate = new DateOnly(2026, 10, 5) },
            new Journal { TenantId = 101, JournalNumber = "J-OTHER", SourceType = "UnrelatedSource",
                SourceId = 99, State = JournalState.Posted, JournalDate = new DateOnly(2026, 10, 5) });
        await db.SaveChangesAsync();
        var service = new V3EnterpriseReconciliationRegisterService(db, new Actor(101, "TenantAdmin"));
        var result = await service.GetAsync("stock", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(0, result.Data!.TotalCount);
        Assert.Equal(1, result.Data.OrphanPostedJournalCount);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(403, (await new V3EnterpriseReconciliationRegisterService(
            db, new Actor(999, "TenantAdmin")).GetAsync("stock",
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))).StatusCode);
    }

    [Fact]
    public async Task Unlinked_movement_is_not_misrepresented_as_unposted_finance()
    {
        await using var db = Open(101);
        var m = new InventoryMovement { TenantId = 101, MovementNumber = "M1",
            MovementTypeCode = "Issue", StateCode = "Posted", PostedAt = DateTime.UtcNow };
        db.InventoryMovements.Add(m);
        await db.SaveChangesAsync();
        // The trace correctly rejects a posted movement without stock-line evidence.
        // This test isolates missing Finance linkage, so provide a valid posted line.
        db.InventoryMovementLines.Add(new InventoryMovementLine {
            TenantId = 101, InventoryMovementId = m.Id, InventoryItemId = 12,
            LineNo = 1, Quantity = 1m, UnitCost = 20m
        });
        await db.SaveChangesAsync();
        var result = await new V3EnterpriseLedgerTraceService(
            db, new Actor(101, "TenantAdmin")).GetAsync(m.PublicId);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("NoExplicitReference", result.Data!.FinanceEvidenceStatus);
    }

    [Fact]
    public async Task Central_stock_register_checks_original_reversal_lot_cost_and_duplicate_evidence()
    {
        await using var db = Open(101);
        var at = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
        var location = new InventoryLocation { Id = 4, TenantId = 101, Name = "Main store", Code = "MAIN" };
        var issue = new InventoryMovement { Id = 31, TenantId = 101,
            MovementNumber = "ISS-31", MovementTypeCode = "Issue",
            FromLocationId = 4, StateCode = "Posted", PostedAt = at, MovementAt = at };
        var reversal = new InventoryMovement { Id = 32, TenantId = 101,
            MovementNumber = "REV-32", MovementTypeCode = "ReversalIssue",
            SourceType = "MovementReversal", SourceId = 31, ReversalOfMovementId = 31,
            FromLocationId = 4, StateCode = "Posted", PostedAt = at, MovementAt = at };
        db.InventoryLocations.Add(location);
        db.InventoryMovements.AddRange(issue, reversal);
        db.InventoryMovementLines.AddRange(
            new InventoryMovementLine { Id = 41, TenantId = 101, InventoryMovementId = 31,
                LineNo = 1, InventoryItemId = 9, Quantity = 3m, UnitCost = 4m, LotOrBatchNo = "LOT-A" },
            new InventoryMovementLine { Id = 42, TenantId = 101, InventoryMovementId = 32,
                LineNo = 1, InventoryItemId = 9, Quantity = 3m, UnitCost = 4m, LotOrBatchNo = "LOT-A" });
        await db.SaveChangesAsync();
        var register = new V3EnterpriseReconciliationRegisterService(db, new Actor(101, "TenantAdmin"));
        var valid = await register.GetAsync("stock", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(200, valid.StatusCode);
        Assert.Equal("NoExplicitReference", Assert.Single(valid.Data!.Items, x => x.Number == "REV-32").EvidenceStatus);

        // A second posted compensation with a different request ID must not
        // make either reversal appear reconciled in historical reporting.
        db.InventoryMovements.Add(new InventoryMovement { Id = 33, TenantId = 101,
            MovementNumber = "REV-33", MovementTypeCode = "ReversalIssue",
            SourceType = "MovementReversal", SourceId = 31, ReversalOfMovementId = 31,
            FromLocationId = 4, StateCode = "Posted", PostedAt = at, MovementAt = at });
        db.InventoryMovementLines.Add(new InventoryMovementLine { Id = 43, TenantId = 101,
            InventoryMovementId = 33, LineNo = 1, InventoryItemId = 9,
            Quantity = 3m, UnitCost = 4m, LotOrBatchNo = "LOT-A" });
        await db.SaveChangesAsync();
        var duplicate = await register.GetAsync("stock", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal("EvidenceConflict", Assert.Single(duplicate.Data!.Items, x => x.Number == "REV-32").EvidenceStatus);
        Assert.Equal("EvidenceConflict", Assert.Single(duplicate.Data.Items, x => x.Number == "REV-33").EvidenceStatus);
    }

    [Theory]
    [InlineData(5, "LOT-A")]
    [InlineData(4, "LOT-B")]
    public async Task Central_stock_register_rejects_balanced_but_changed_reversal_line(int unitCost, string lot)
    {
        await using var db = Open(101);
        var at = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
        db.InventoryLocations.Add(new InventoryLocation { Id = 4, TenantId = 101, Name = "Main store", Code = "MAIN" });
        db.InventoryMovements.AddRange(
            new InventoryMovement { Id = 31, TenantId = 101, MovementNumber = "ISS-31",
                MovementTypeCode = "Issue", FromLocationId = 4,
                StateCode = "Posted", PostedAt = at, MovementAt = at },
            new InventoryMovement { Id = 32, TenantId = 101, MovementNumber = "REV-32",
                MovementTypeCode = "ReversalIssue", SourceType = "MovementReversal",
                SourceId = 31, ReversalOfMovementId = 31, FromLocationId = 4,
                StateCode = "Posted", PostedAt = at, MovementAt = at });
        db.InventoryMovementLines.AddRange(
            new InventoryMovementLine { Id = 41, TenantId = 101, InventoryMovementId = 31,
                LineNo = 1, InventoryItemId = 9, Quantity = 3m, UnitCost = 4m, LotOrBatchNo = "LOT-A" },
            new InventoryMovementLine { Id = 42, TenantId = 101, InventoryMovementId = 32,
                LineNo = 1, InventoryItemId = 9, Quantity = 3m, UnitCost = unitCost, LotOrBatchNo = lot });
        await db.SaveChangesAsync();
        var register = new V3EnterpriseReconciliationRegisterService(db, new Actor(101, "TenantAdmin"));
        var page = await register.GetAsync("stock", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal("EvidenceConflict", Assert.Single(page.Data!.Items, x => x.Number == "REV-32").EvidenceStatus);
    }
}
