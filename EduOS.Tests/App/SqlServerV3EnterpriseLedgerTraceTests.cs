using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Enterprise;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerV3EnterpriseLedgerTraceTests
{
    private sealed class Actor(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 1;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    [Fact]
    public async Task Explicit_stock_finance_lineage_is_tenant_isolated_and_preserves_deleted_evidence()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, foreignId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "V3-TRACE-" + suffix,
                Name = "Enterprise trace SQL", Email = "trace-" + suffix + "@example.test",
                State = TenantState.Active };
            var foreign = new Tenant { Code = "V3-OTHER-" + suffix,
                Name = "Foreign enterprise trace SQL", Email = "foreign-" + suffix + "@example.test",
                State = TenantState.Active };
            root.Tenants.AddRange(tenant, foreign);
            await root.SaveChangesAsync();
            tenantId = tenant.Id;
            foreignId = foreign.Id;
        }

        Guid movementReference;
        long journalId;
        await using (var db = Open(options, tenantId))
        {
            var location = new InventoryLocation { TenantId = tenantId,
                Code = "LOC-" + suffix, Name = "Campus store" };
            var item = new InventoryItem { TenantId = tenantId,
                Code = "ITM-" + suffix, Name = "Laboratory supplies" };
            var period = new AccountingPeriod { TenantId = tenantId,
                Name = "October 2026 " + suffix,
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) };
            var debit = new Account { TenantId = tenantId, Code = "DR-" + suffix,
                Name = "Materials", AccountType = AccountType.Asset };
            var credit = new Account { TenantId = tenantId, Code = "CR-" + suffix,
                Name = "Clearing", AccountType = AccountType.Liability };
            db.InventoryLocations.Add(location);
            db.InventoryItems.Add(item);
            db.AccountingPeriods.Add(period);
            db.Accounts.AddRange(debit, credit);
            await db.SaveChangesAsync();
            var movement = new InventoryMovement { TenantId = tenantId,
                MovementNumber = "MV-" + suffix, ClientRequestId = Guid.NewGuid(),
                MovementTypeCode = "Receipt", StateCode = "Posted",
                ToLocationId = location.Id, PostedAt = DateTime.UtcNow,
                SourceType = "PurchaseReceipt", SourceId = 12345 };
            db.InventoryMovements.Add(movement);
            await db.SaveChangesAsync();
            movementReference = movement.PublicId;
            db.InventoryMovementLines.Add(new InventoryMovementLine {
                TenantId = tenantId, InventoryMovementId = movement.Id,
                InventoryItemId = item.Id, LineNo = 1, Quantity = 3m, UnitCost = 20m
            });
            var journal = new Journal { TenantId = tenantId, JournalNumber = "JV-" + suffix,
                AccountingPeriodId = period.Id, JournalDate = new DateOnly(2026, 10, 30),
                SourceType = "InventoryMovement", SourceId = movement.Id,
                State = JournalState.Posted };
            db.Journals.Add(journal);
            await db.SaveChangesAsync();
            journalId = journal.Id;
            db.JournalLines.AddRange(
                new JournalLine { TenantId = tenantId, JournalId = journal.Id, LineNo = 1,
                    AccountId = debit.Id, DebitAmount = 60m },
                new JournalLine { TenantId = tenantId, JournalId = journal.Id, LineNo = 2,
                    AccountId = credit.Id, CreditAmount = 60m });
            await db.SaveChangesAsync();
        }

        await using (var db = Open(options, tenantId))
        {
            var service = new V3EnterpriseLedgerTraceService(db, new Actor(tenantId));
            var first = await service.GetAsync(movementReference);
            Assert.Equal(200, first.StatusCode);
            Assert.Equal(60m, first.Data!.RecordedMovementValue);
            Assert.Equal("ExplicitBalancedReference", first.Data.FinanceEvidenceStatus);
            Assert.Single(first.Data.Journals);
            Assert.True(first.Data.Journals[0].Balanced);
            var replay = await service.GetAsync(movementReference);
            Assert.Equal(first.Data.Journals[0].Reference, replay.Data!.Journals[0].Reference);
            Assert.Equal(404, (await service.GetAsync(Guid.NewGuid())).StatusCode);
            var register = new V3EnterpriseReconciliationRegisterService(db, new Actor(tenantId));
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var listing = await register.GetAsync("stock", today.AddDays(-1), today.AddDays(1));
            Assert.Equal(200, listing.StatusCode);
            var row = Assert.Single(listing.Data!.Items.Where(x => x.Reference == movementReference));
            Assert.Equal(60m, row.RecordedValue);
            Assert.Equal("ExplicitBalancedReference", row.EvidenceStatus);
            Assert.Equal(400, (await register.GetAsync("stock", today.AddDays(-400), today)).StatusCode);
            Assert.Equal(400, (await register.GetAsync("stock", today, today, pageSize: 100)).StatusCode);
        }

        await using (var foreign = Open(options, foreignId))
        {
            var denied = await new V3EnterpriseLedgerTraceService(foreign, new Actor(foreignId))
                .GetAsync(movementReference);
            Assert.Equal(404, denied.StatusCode);
            var foreignRegister = await new V3EnterpriseReconciliationRegisterService(
                foreign, new Actor(foreignId)).GetAsync("stock",
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
            Assert.Equal(200, foreignRegister.StatusCode);
            Assert.DoesNotContain(foreignRegister.Data!.Items,
                x => x.Reference == movementReference);
        }

        await using (var db = Open(options, tenantId))
        {
            // Deliberately simulate a legacy tombstone in disposable SQL, bypassing
            // the V1 SaveChanges guard that correctly forbids edits to posted journals.
            var lineId = await db.JournalLines.Where(x => x.JournalId == journalId)
                .OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            var creditLineId = await db.JournalLines.Where(x =>
                x.JournalId == journalId && x.CreditAmount > 0m)
                .Select(x => x.Id).SingleAsync();
            await db.JournalLines.Where(x => x.Id == creditLineId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreditAmount, 59m));
            var register = new V3EnterpriseReconciliationRegisterService(db, new Actor(tenantId));
            var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
            var unbalanced = await register.GetAsync("stock", from, to);
            Assert.Equal("JournalImbalance", Assert.Single(unbalanced.Data!.Items,
                x => x.Reference == movementReference).EvidenceStatus);
            await db.JournalLines.Where(x => x.Id == creditLineId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreditAmount, 60m));
            await db.JournalLines.Where(x => x.Id == lineId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            Assert.Equal("EvidenceConflict", Assert.Single(
                (await register.GetAsync("stock", from, to)).Data!.Items,
                x => x.Reference == movementReference).EvidenceStatus);
            Assert.Equal(409, (await new V3EnterpriseLedgerTraceService(db, new Actor(tenantId))
                .GetAsync(movementReference)).StatusCode);
        }
    }
}
