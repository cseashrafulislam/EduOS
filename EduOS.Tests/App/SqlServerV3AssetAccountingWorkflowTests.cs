using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3ProcurementPurchaseOrderTests
{
    private static AssetAccountingService AssetFinance(EduOSDbContext db, long tenant, long actor)
    {
        var role = new Actor(tenant, actor);
        return new AssetAccountingService(db, role, TimeProvider.System,
            new InventoryMovementService(db, role, TimeProvider.System,
                NullLogger<InventoryMovementService>.Instance),
            new AssetLifecycleService(db, role, TimeProvider.System,
                NullLogger<AssetLifecycleService>.Instance),
            NullLogger<AssetAccountingService>.Instance);
    }

    [Fact]
    public async Task SQL_asset_depreciation_impairment_disposal_maker_checker_and_exact_GL_contra()
    {
        var conn = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn)) return;
        var opts = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(conn).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthEnd = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        var inService = new DateOnly(monthEnd.Year, monthEnd.Month, 1);
        long tenantId, makerId, checkerId, periodId;
        Guid assetReference;
        await using (var root = new EduOSDbContext(opts))
        {
            var tenant = new Tenant { Code = "AOPS-" + suffix, Name = "Asset operations SQL",
                Email = "aops-" + suffix + "@example.test", State = TenantState.Active };
            var maker = new ApplicationUser { UserName = "aops-m-" + suffix,
                NormalizedUserName = ("aops-m-" + suffix).ToUpperInvariant(),
                Email = "aops-m-" + suffix + "@example.test", FullName = "Asset maker" };
            var checker = new ApplicationUser { UserName = "aops-c-" + suffix,
                NormalizedUserName = ("aops-c-" + suffix).ToUpperInvariant(),
                Email = "aops-c-" + suffix + "@example.test", FullName = "Asset checker" };
            root.AddRange(tenant, maker, checker);
            await root.SaveChangesAsync();
            tenantId = tenant.Id; makerId = maker.Id; checkerId = checker.Id;
        }
        await using (var db = Open(opts, tenantId, makerId))
        {
            var campus = new Campus { TenantId = tenantId, Code = "AOPS-C-" + suffix,
                Name = "Asset campus", IsActive = true };
            var category = new AssetCategory { TenantId = tenantId,
                Code = "AOPS-CAT-" + suffix, Name = "Equipment" };
            var period = new AccountingPeriod { TenantId = tenantId, Name = "Asset open " + suffix,
                StartDate = inService, EndDate = today.AddMonths(2), IsClosed = false };
            db.AddRange(campus, category, period);
            await db.SaveChangesAsync(); periodId = period.Id;
            var asset = new Asset { TenantId = tenantId, CampusId = campus.Id,
                AssetCategoryId = category.Id, AssetTag = "AOPS-" + suffix,
                Name = "Laboratory centrifuge", PurchaseCost = 1200m };
            db.Assets.Add(asset);
            var accounts = new[]
            {
                new Account { TenantId = tenantId, Code = "AOPS-FA-" + suffix,
                    Name = "Fixed assets", AccountType = AccountType.Asset },
                new Account { TenantId = tenantId, Code = "AOPS-AD-" + suffix,
                    Name = "Accumulated depreciation", AccountType = AccountType.Asset },
                new Account { TenantId = tenantId, Code = "AOPS-DE-" + suffix,
                    Name = "Depreciation expense", AccountType = AccountType.Expense },
                new Account { TenantId = tenantId, Code = "AOPS-IE-" + suffix,
                    Name = "Impairment expense", AccountType = AccountType.Expense },
                new Account { TenantId = tenantId, Code = "AOPS-GI-" + suffix,
                    Name = "Disposal gain", AccountType = AccountType.Income },
                new Account { TenantId = tenantId, Code = "AOPS-LE-" + suffix,
                    Name = "Disposal loss", AccountType = AccountType.Expense },
                new Account { TenantId = tenantId, Code = "AOPS-CL-" + suffix,
                    Name = "Proceeds clearing", AccountType = AccountType.Asset }
            };
            db.Accounts.AddRange(accounts);
            await db.SaveChangesAsync(); assetReference = asset.PublicId;
            db.AssetBooks.Add(new AssetBook
            {
                TenantId = tenantId, CampusId = campus.Id, AssetId = asset.Id,
                InServiceDate = inService, MethodCode = "StraightLine",
                UsefulLifeMonths = 12, CostBasis = 1200m, SalvageValue = 0m,
                PolicyVersion = 1, IsActive = true, CostAccountId = accounts[0].Id,
                AccumulatedDepreciationAccountId = accounts[1].Id,
                DepreciationExpenseAccountId = accounts[2].Id,
                ImpairmentExpenseAccountId = accounts[3].Id,
                DisposalGainAccountId = accounts[4].Id, DisposalLossAccountId = accounts[5].Id
            });
            await db.SaveChangesAsync();
        }
        AssetValuationRequest Depreciation() => new()
        {
            ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
            EffectiveDate = monthEnd, ValuationMode = "Automatic", Amount = 0m,
            Reason = "Monthly straight-line depreciation per approved asset schedule"
        };
        var depreciationRequest = Depreciation();
        long depId; string depVersion;
        await using (var db = Open(opts, tenantId, makerId))
        {
            var service = AssetFinance(db, tenantId, makerId);
            var submitted = await service.RequestDepreciationAsync(assetReference, depreciationRequest);
            Assert.True(submitted.Success, submitted.Message); depId = submitted.Data!.Id;
            depVersion = submitted.Data.RowVersion;
            Assert.Equal(100m, submitted.Data.Amount);
            Assert.Equal(depId, (await service.RequestDepreciationAsync(assetReference,
                depreciationRequest)).Data!.Id);
            Assert.Equal(404, (await service.RequestDepreciationAsync(
                Guid.NewGuid(), depreciationRequest)).StatusCode);
            Assert.Single(await db.AssetDepreciationRuns.AsNoTracking().ToListAsync());
            Assert.Equal(409, (await service.ReviewDepreciationAsync(depId,
                new AssetReviewRequest { RowVersion = depVersion, Approve = true,
                    Reason = "Cannot approve self-submitted depreciation" })).StatusCode);
        }
        await using (var db = Open(opts, tenantId, checkerId))
        {
            var result = await AssetFinance(db, tenantId, checkerId).ReviewDepreciationAsync(depId,
                new AssetReviewRequest { RowVersion = depVersion, Approve = true,
                    Reason = "Independent monthly depreciation finance approval" });
            Assert.True(result.Success, result.Message);
            Assert.Equal("Posted", result.Data!.State);
            Assert.NotNull(result.Data.JournalReference);
            Assert.Equal(409, (await AssetFinance(db, tenantId, checkerId).ReviewDepreciationAsync(depId,
                new AssetReviewRequest { RowVersion = depVersion, Approve = true,
                    Reason = "Duplicate depreciation posting should conflict" })).StatusCode);
        }
        // A future-effective schedule amendment must preserve historical 100 BDT
        // depreciation and require a separate Finance approver.
        var policyEffective = new DateOnly(today.Year, today.Month, 1);
        Guid policyReference;
        string policyVersion;
        await using (var db = Open(opts, tenantId, makerId))
        {
            var finance = AssetFinance(db, tenantId, makerId);
            var statement = (await finance.StatementAsync(assetReference)).Data!;
            var proposed = await finance.RequestDepreciationPolicyAsync(assetReference,
                new CreateAssetDepreciationPolicyRequest
                {
                    ClientRequestId = Guid.NewGuid(), AssetBookRowVersion = statement.AssetBookRowVersion,
                    AccountingPeriodId = periodId, EffectiveFrom = policyEffective,
                    Mode = "AutoPlusManual", NewPolicyVersion = 2,
                    RemainingLifeCorrectionMonths = 1, NewSalvageValue = 50m,
                    Reason = "Board approved prospective residual-value and useful-life revision"
                });
            Assert.True(proposed.Success, proposed.Message);
            Assert.Equal("Submitted", proposed.Data!.State);
            Assert.Equal(12, proposed.Data.ApprovedRemainingMonths);
            policyReference = proposed.Data.Reference;
            policyVersion = proposed.Data.RowVersion;
            Assert.Equal(409, (await finance.ReviewDepreciationPolicyAsync(
                policyReference, new AssetReviewRequest { RowVersion = policyVersion,
                    Approve = true, Reason = "Maker cannot approve a new depreciation policy" })).StatusCode);
        }
        await using (var db = Open(opts, tenantId, checkerId))
        {
            var finance = AssetFinance(db, tenantId, checkerId);
            var approved = await finance.ReviewDepreciationPolicyAsync(policyReference,
                new AssetReviewRequest { RowVersion = policyVersion, Approve = true,
                    Reason = "Independent verified prospective schedule board authorization" });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal("Approved", approved.Data!.State);
            var statement = (await finance.StatementAsync(assetReference)).Data!;
            Assert.Equal(100m, statement.PostedDepreciation);
            Assert.Equal(2, statement.PolicyVersion);
            Assert.Equal("AutoPlusManual", statement.PolicyMode);
            Assert.Equal(50m, statement.SalvageValue);
            Assert.Single(statement.PolicyHistory);
            Assert.Equal(409, (await finance.ReviewDepreciationPolicyAsync(policyReference,
                new AssetReviewRequest { RowVersion = policyVersion, Approve = true,
                    Reason = "Do not double approve a policy revision" })).StatusCode);
        }
        long impairmentId; string impairmentVersion;
        await using (var db = Open(opts, tenantId, makerId))
        {
            var finance = AssetFinance(db, tenantId, makerId);
            var impairmentRequest = new AssetValuationRequest
            {
                ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
                EffectiveDate = today, ValuationMode = "ManualFinal", Amount = 25m,
                Reason = "Signed impairment inspection values the damaged component"
            };
            var submitted = await finance.RequestImpairmentAsync(assetReference, impairmentRequest);
            Assert.True(submitted.Success, submitted.Message);
            impairmentId = submitted.Data!.Id; impairmentVersion = submitted.Data.RowVersion;
            Assert.Equal(impairmentId, (await finance.RequestImpairmentAsync(
                assetReference, impairmentRequest)).Data!.Id);
            Assert.Equal(404, (await finance.RequestImpairmentAsync(
                Guid.NewGuid(), impairmentRequest)).StatusCode);
            Assert.Single(await db.AssetImpairments.AsNoTracking().ToListAsync());
        }
        await using (var db = Open(opts, tenantId, checkerId))
        {
            var result = await AssetFinance(db, tenantId, checkerId).ReviewImpairmentAsync(
                impairmentId, new AssetReviewRequest { RowVersion = impairmentVersion,
                    Approve = true, Reason = "Independent fixed-asset valuation review approved" });
            Assert.True(result.Success, result.Message);
            var statement = await AssetFinance(db, tenantId, checkerId).StatementAsync(assetReference);
            Assert.True(statement.Success, statement.Message);
            Assert.Equal(100m, statement.Data!.PostedDepreciation);
            Assert.Equal(25m, statement.Data.PostedImpairment);
            Assert.Equal(1075m, statement.Data.CarryingValue);
        }
        long disposalId; string disposalVersion;
        await using (var db = Open(opts, tenantId, makerId))
        {
            var clearing = await db.Accounts.Where(x => x.Code == "AOPS-CL-" + suffix)
                .Select(x => x.Id).SingleAsync();
            var finance = AssetFinance(db, tenantId, makerId);
            var disposalRequest = new AssetValuationRequest
            {
                ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
                EffectiveDate = today, ValuationMode = "ManualFinal", Amount = 600m,
                ProceedsClearingAccountId = clearing,
                Reason = "Management-approved disposal with invoiced clearing proceeds"
            };
            var submitted = await finance.RequestDisposalAsync(assetReference, disposalRequest);
            Assert.True(submitted.Success, submitted.Message);
            disposalId = submitted.Data!.Id; disposalVersion = submitted.Data.RowVersion;
            Assert.Equal(disposalId, (await finance.RequestDisposalAsync(
                assetReference, disposalRequest)).Data!.Id);
            Assert.Equal(404, (await finance.RequestDisposalAsync(
                Guid.NewGuid(), disposalRequest)).StatusCode);
            Assert.Single(await db.AssetDisposals.AsNoTracking().ToListAsync());
        }
        // Closing the period after maker submission must stop the checker from
        // creating any disposal GL. The same pending source can be reviewed
        // only after an authorized period reopen, with no synthetic reversal.
        await using (var close = Open(opts, tenantId, makerId))
            Assert.Equal(1, await close.AccountingPeriods.Where(x => x.Id == periodId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsClosed, true)));
        await using (var restricted = Open(opts, tenantId + 99999, checkerId))
            Assert.Equal(404, (await AssetFinance(restricted, tenantId + 99999, checkerId)
                .ReviewDisposalAsync(disposalId, new AssetReviewRequest
                {
                    RowVersion = disposalVersion, Approve = true,
                    Reason = "Foreign institution cannot approve the disposal"
                })).StatusCode);
        await using (var blocked = Open(opts, tenantId, checkerId))
        {
            var denied = await AssetFinance(blocked, tenantId, checkerId)
                .ReviewDisposalAsync(disposalId, new AssetReviewRequest
                {
                    RowVersion = disposalVersion, Approve = true,
                    Reason = "Closed financial period must prevent disposal posting"
                });
            Assert.Equal(409, denied.StatusCode);
            Assert.Equal("Submitted", (await blocked.AssetDisposals.AsNoTracking()
                .SingleAsync(x => x.Id == disposalId)).StateCode);
            Assert.False(await blocked.Journals.AsNoTracking()
                .AnyAsync(x => x.SourceType == "AssetDisposal"));
        }
        await using (var reopen = Open(opts, tenantId, makerId))
            Assert.Equal(1, await reopen.AccountingPeriods.Where(x => x.Id == periodId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsClosed, false)));
        await using (var db = Open(opts, tenantId, checkerId))
        {
            var result = await AssetFinance(db, tenantId, checkerId).ReviewDisposalAsync(
                disposalId, new AssetReviewRequest { RowVersion = disposalVersion,
                    Approve = true, Reason = "Independent disposal approval and custody clearance" });
            Assert.True(result.Success, result.Message);
            Assert.Equal("Posted", result.Data!.State);
            Assert.False((await db.AssetBooks.SingleAsync()).IsActive);
            var journal = await db.Journals.SingleAsync(x => x.SourceType == "AssetDisposal");
            var lines = await db.JournalLines.Where(x => x.JournalId == journal.Id).ToListAsync();
            Assert.Equal(lines.Sum(x => x.DebitAmount), lines.Sum(x => x.CreditAmount));
            Assert.Equal(1175m, lines.Single(x => x.CreditAmount == 1175m).CreditAmount);
        }
        Guid reversalReference; string reversalVersion;
        AssetReversalRequest disposalReversalCommand;
        await using (var db = Open(opts, tenantId, makerId))
        {
            var statement = (await AssetFinance(db, tenantId, makerId).StatementAsync(assetReference)).Data!;
            var source = Assert.Single(statement.Events.Where(x => x.Kind == "Disposal"));
            disposalReversalCommand = new AssetReversalRequest
            {
                ClientRequestId = Guid.NewGuid(), OriginalRowVersion = source.RowVersion,
                AccountingPeriodId = periodId, ReversalDate = today,
                Reason = "Independent correction of erroneous asset disposal evidence"
            };
            var reverse = await AssetFinance(db, tenantId, makerId).RequestEventReversalAsync(
                "Disposal", disposalId, disposalReversalCommand);
            Assert.True(reverse.Success, reverse.Message);
            reversalReference = reverse.Data!.PendingReversalReference!.Value;
            reversalVersion = reverse.Data.PendingReversalRowVersion!;
            var pendingReplay = await AssetFinance(db, tenantId, makerId)
                .RequestEventReversalAsync("Disposal", disposalId, disposalReversalCommand);
            Assert.True(pendingReplay.Success, pendingReplay.Message);
            Assert.Equal(reversalReference, pendingReplay.Data!.PendingReversalReference);
        }
        await using (var db = Open(opts, tenantId, checkerId))
        {
            var result = await AssetFinance(db, tenantId, checkerId).ReviewEventReversalAsync(
                "Disposal", disposalId, reversalReference,
                new AssetReviewRequest { RowVersion = reversalVersion, Approve = true,
                    Reason = "Independent approval of exact disposal GL correction" });
            Assert.True(result.Success, result.Message);
            Assert.True((await db.AssetBooks.SingleAsync()).IsActive);
            Assert.Equal("Available", (await db.Assets.SingleAsync()).StateCode);
            var contra = await db.Journals.SingleAsync(x => x.PublicId == reversalReference);
            var original = await db.Journals.SingleAsync(x => x.SourceType == "AssetDisposal");
            Assert.Equal(original.Id, contra.ReversalOfJournalId);
            var o = await db.JournalLines.Where(x => x.JournalId == original.Id).OrderBy(x => x.LineNo).ToListAsync();
            var r = await db.JournalLines.Where(x => x.JournalId == contra.Id).OrderBy(x => x.LineNo).ToListAsync();
            Assert.Equal(o.Count, r.Count);
            for (var i = 0; i < o.Count; i++)
            {
                Assert.Equal(o[i].DebitAmount, r[i].CreditAmount);
                Assert.Equal(o[i].CreditAmount, r[i].DebitAmount);
            }
            Assert.Equal(1075m, (await AssetFinance(db, tenantId, checkerId).StatementAsync(
                assetReference)).Data!.CarryingValue);
            Assert.Equal(409, (await AssetFinance(db, tenantId, checkerId)
                .RequestEventReversalAsync("Disposal", disposalId, disposalReversalCommand)).StatusCode);
        }
        await using (var db = Open(opts, tenantId, makerId))
        {
            var finance = AssetFinance(db, tenantId, makerId);
            var postedReplay = await finance.RequestEventReversalAsync(
                "Disposal", disposalId, disposalReversalCommand);
            Assert.True(postedReplay.Success, postedReplay.Message);
            Assert.Equal("Reversed", postedReplay.Data!.State);
            Assert.Equal(reversalReference, postedReplay.Data.ReversalJournalReference);
            Assert.Equal(409, (await finance.RequestEventReversalAsync(
                "Disposal", disposalId, new AssetReversalRequest
                {
                    ClientRequestId = disposalReversalCommand.ClientRequestId,
                    OriginalRowVersion = disposalReversalCommand.OriginalRowVersion,
                    AccountingPeriodId = disposalReversalCommand.AccountingPeriodId,
                    ReversalDate = disposalReversalCommand.ReversalDate,
                    Reason = "Changed reason must never claim the posted reversal key"
                })).StatusCode);
            Assert.Single(await db.Journals.AsNoTracking().Where(x =>
                x.SourceType == "AssetDisposalReversal" &&
                x.PublicId == reversalReference && x.State == JournalState.Posted).ToListAsync());
        }
    }
}
