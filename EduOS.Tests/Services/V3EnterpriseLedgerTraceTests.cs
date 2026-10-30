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
}
