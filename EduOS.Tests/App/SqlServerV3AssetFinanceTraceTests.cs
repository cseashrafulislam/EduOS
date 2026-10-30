using EduOS.Core.Entities.Accounting;
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
public sealed class SqlServerV3AssetFinanceTraceTests
{
    private sealed class Actor(long tenant, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 1;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Student"];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
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
    public async Task Depreciation_run_to_posted_journal_is_reconciled_and_tenant_isolated()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, foreignId, journalId, lineId;
        var requestId = Guid.NewGuid();

        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "V3-AFIN-" + suffix,
                Name = "Asset finance SQL", Email = "afin-" + suffix + "@example.test",
                State = TenantState.Active };
            var foreign = new Tenant { Code = "V3-AFINX-" + suffix,
                Name = "Other asset finance SQL", Email = "afinx-" + suffix + "@example.test",
                State = TenantState.Active };
            root.Tenants.AddRange(tenant, foreign);
            await root.SaveChangesAsync();
            tenantId = tenant.Id;
            foreignId = foreign.Id;
        }
        await using (var db = Open(options, tenantId))
        {
            var campus = new Campus { TenantId = tenantId, Code = "AF-" + suffix,
                Name = "Asset finance campus", IsActive = true };
            var category = new AssetCategory { TenantId = tenantId,
                Code = "AC-" + suffix, Name = "Asset finance category" };
            var period = new AccountingPeriod { TenantId = tenantId,
                Name = "Asset finance " + suffix, StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31) };
            var debit = new Account { TenantId = tenantId, Code = "AD-" + suffix,
                Name = "Depreciation expense", AccountType = AccountType.Expense };
            var credit = new Account { TenantId = tenantId, Code = "ACR-" + suffix,
                Name = "Accumulated depreciation", AccountType = AccountType.Asset };
            db.AddRange(campus, category, period, debit, credit);
            await db.SaveChangesAsync();
            var asset = new Asset { TenantId = tenantId, CampusId = campus.Id,
                AssetCategoryId = category.Id, AssetTag = "AF-" + suffix,
                Name = "Laboratory equipment", PurchaseCost = 1000m };
            db.Assets.Add(asset);
            await db.SaveChangesAsync();
            var book = new AssetBook { TenantId = tenantId, CampusId = campus.Id,
                AssetId = asset.Id, InServiceDate = new DateOnly(2026, 1, 1),
                MethodCode = "StraightLine", UsefulLifeMonths = 36,
                CostBasis = 1000m, SalvageValue = 100m, PolicyVersion = 1, IsActive = true };
            var journal = new Journal { TenantId = tenantId, CampusId = campus.Id,
                JournalNumber = "AF-JV-" + suffix, AccountingPeriodId = period.Id,
                JournalDate = new DateOnly(2026, 10, 30), State = JournalState.Posted };
            db.AddRange(book, journal);
            await db.SaveChangesAsync();
            journalId = journal.Id;
            var run = new AssetDepreciationRun { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = requestId, AccountingPeriodId = period.Id,
                PeriodEnd = new DateOnly(2026, 10, 31), StateCode = "Posted",
                TotalAmount = 25m, JournalId = journal.Id, PostedAt = DateTime.UtcNow };
            db.AssetDepreciationRuns.Add(run);
            await db.SaveChangesAsync();
            var line = new AssetDepreciationLine { TenantId = tenantId,
                AssetDepreciationRunId = run.Id, AssetBookId = book.Id,
                PeriodEnd = run.PeriodEnd, Amount = 25m,
                OpeningAccumulated = 0m, ClosingAccumulated = 25m,
                ClosingCarryingValue = 975m };
            db.AssetDepreciationLines.Add(line);
            db.JournalLines.AddRange(
                new JournalLine { TenantId = tenantId, JournalId = journal.Id, LineNo = 1,
                    AccountId = debit.Id, DebitAmount = 25m },
                new JournalLine { TenantId = tenantId, JournalId = journal.Id, LineNo = 2,
                    AccountId = credit.Id, CreditAmount = 25m });
            await db.SaveChangesAsync();
            lineId = line.Id;
        }
        await using (var db = Open(options, tenantId))
        {
            var service = new V3EnterpriseAssetFinanceTraceService(db, new Actor(tenantId));
            var result = await service.GetAsync(requestId);
            Assert.Equal(200, result.StatusCode);
            Assert.Equal("ExplicitBalancedReference", result.Data!.FinanceEvidenceStatus);
            Assert.Equal(25m, result.Data.DepreciationLinesAmount);
            Assert.Equal(1, result.Data.DepreciationLineCount);
            Assert.Single(result.Data.Journals);
            Assert.True(result.Data.Journals[0].Balanced);
            Assert.Equal(result.Data.Journals[0].Reference,
                (await service.GetAsync(requestId)).Data!.Journals[0].Reference);
            var register = new V3EnterpriseReconciliationRegisterService(db, new Actor(tenantId));
            var listing = await register.GetAsync("asset", new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 31));
            Assert.Equal(200, listing.StatusCode);
            var row = Assert.Single(listing.Data!.Items.Where(x => x.Reference == requestId));
            Assert.Equal(25m, row.RecordedValue);
            Assert.Equal("ExplicitBalancedReference", row.EvidenceStatus);

            // Two runs share one SQL report page. Corrupting the second journal
            // must never change the first run's balanced status.
            var periodId = await db.AccountingPeriods.Select(x => x.Id).SingleAsync();
            var campusId = await db.Campuses.Select(x => x.Id).SingleAsync();
            var bookId = await db.AssetBooks.Select(x => x.Id).SingleAsync();
            var accounts = await db.Accounts.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            var otherJournal = new Journal { TenantId = tenantId, CampusId = campusId,
                JournalNumber = "AF-JV2-" + Guid.NewGuid().ToString("N")[..12],
                AccountingPeriodId = periodId, JournalDate = new DateOnly(2026, 10, 30),
                State = JournalState.Posted };
            db.Journals.Add(otherJournal);
            await db.SaveChangesAsync();
            var otherReference = Guid.NewGuid();
            var otherRun = new AssetDepreciationRun { TenantId = tenantId, CampusId = campusId,
                ClientRequestId = otherReference, AccountingPeriodId = periodId,
                PeriodEnd = new DateOnly(2026, 10, 30), StateCode = "Posted",
                TotalAmount = 25m, JournalId = otherJournal.Id };
            db.AssetDepreciationRuns.Add(otherRun);
            await db.SaveChangesAsync();
            db.AssetDepreciationLines.Add(new AssetDepreciationLine {
                TenantId = tenantId, AssetDepreciationRunId = otherRun.Id,
                AssetBookId = bookId, PeriodEnd = otherRun.PeriodEnd,
                Amount = 25m, ClosingAccumulated = 25m, ClosingCarryingValue = 975m });
            db.JournalLines.AddRange(
                new JournalLine { TenantId = tenantId, JournalId = otherJournal.Id,
                    LineNo = 1, AccountId = accounts[0], DebitAmount = 25m },
                new JournalLine { TenantId = tenantId, JournalId = otherJournal.Id,
                    LineNo = 2, AccountId = accounts[1], CreditAmount = 25m });
            await db.SaveChangesAsync();
            await db.JournalLines.Where(x => x.JournalId == otherJournal.Id && x.CreditAmount > 0m)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.CreditAmount, 24m));
            var mixedPage = await register.GetAsync("asset",
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
            Assert.Equal("ExplicitBalancedReference", Assert.Single(mixedPage.Data!.Items,
                x => x.Reference == requestId).EvidenceStatus);
            Assert.Equal("JournalImbalance", Assert.Single(mixedPage.Data.Items,
                x => x.Reference == otherReference).EvidenceStatus);

            Assert.Equal(404, (await register.GetAsync("asset", new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 31), -99)).StatusCode);
            Assert.Equal(403, (await new V3EnterpriseReconciliationRegisterService(
                db, new Actor(tenantId, false)).GetAsync("asset",
                    new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))).StatusCode);
            Assert.Equal(403, (await new V3EnterpriseAssetFinanceTraceService(
                db, new Actor(tenantId, false)).GetAsync(requestId)).StatusCode);
        }
        await using (var db = Open(options, foreignId))
        {
            Assert.Equal(404, (await new V3EnterpriseAssetFinanceTraceService(
                db, new Actor(foreignId)).GetAsync(requestId)).StatusCode);
            var foreignRegister = await new V3EnterpriseReconciliationRegisterService(
                db, new Actor(foreignId)).GetAsync("asset",
                    new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
            Assert.Equal(200, foreignRegister.StatusCode);
            Assert.DoesNotContain(foreignRegister.Data!.Items, x => x.Reference == requestId);
        }
        await using (var db = Open(options, tenantId))
        {
            // Simulate an imported legacy inconsistency in disposable SQL only.
            await db.AssetDepreciationLines.Where(x => x.Id == lineId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Amount, 20m));
            var mismatch = await new V3EnterpriseAssetFinanceTraceService(db, new Actor(tenantId))
                .GetAsync(requestId);
            Assert.Equal("RunLineMismatch", mismatch.Data!.FinanceEvidenceStatus);
            var register = new V3EnterpriseReconciliationRegisterService(db, new Actor(tenantId));
            var from = new DateOnly(2026, 10, 1);
            var to = new DateOnly(2026, 10, 31);
            Assert.Equal("RunLineMismatch", Assert.Single(
                (await register.GetAsync("asset", from, to)).Data!.Items,
                x => x.Reference == requestId).EvidenceStatus);
            var journalLineId = await db.JournalLines.Where(x => x.JournalId == journalId)
                .OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            await db.JournalLines.Where(x => x.Id == journalLineId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            Assert.Equal(409, (await new V3EnterpriseAssetFinanceTraceService(
                db, new Actor(tenantId)).GetAsync(requestId)).StatusCode);
            Assert.Equal("EvidenceConflict", Assert.Single(
                (await register.GetAsync("asset", from, to)).Data!.Items,
                x => x.Reference == requestId).EvidenceStatus);
        }
    }
}
