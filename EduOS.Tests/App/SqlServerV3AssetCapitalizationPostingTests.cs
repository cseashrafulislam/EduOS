using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3ProcurementPurchaseOrderTests
{
    [Fact]
    public async Task SQL_receipt_unit_to_canonical_stock_issue_assetbook_and_GL_is_one_approved_transaction()
    {
        var conn = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn)) return;
        var opts = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(conn).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long tenantId, makerId, checkerId, poId, poLineId, storeId, periodId;
        long[] accounts;
        Guid assetReference, receiptReference;
        await using (var root = new EduOSDbContext(opts))
        {
            var tenant = new Tenant { Code = "ACAP-" + suffix, Name = "Capitalization SQL",
                Email = "acap-" + suffix + "@example.test", State = TenantState.Active };
            var maker = new ApplicationUser { UserName = "acap-m-" + suffix,
                NormalizedUserName = ("acap-m-" + suffix).ToUpperInvariant(),
                Email = "acap-m-" + suffix + "@example.test", FullName = "Capitalization maker" };
            var checker = new ApplicationUser { UserName = "acap-c-" + suffix,
                NormalizedUserName = ("acap-c-" + suffix).ToUpperInvariant(),
                Email = "acap-c-" + suffix + "@example.test", FullName = "Capitalization checker" };
            root.AddRange(tenant, maker, checker);
            await root.SaveChangesAsync();
            tenantId = tenant.Id; makerId = maker.Id; checkerId = checker.Id;
        }
        await using (var db = Open(opts, tenantId, makerId))
        {
            var campus = new Campus { TenantId = tenantId, Code = "ACAP-C-" + suffix,
                Name = "Asset campus", IsActive = true };
            var item = new InventoryItem { TenantId = tenantId, Code = "ACAP-I-" + suffix,
                Name = "Serialized science equipment", IsStockTracked = true,
                IsAssetTracked = true, IsActive = true };
            var category = new AssetCategory { TenantId = tenantId,
                Code = "ACAP-CT-" + suffix, Name = "Laboratory equipment" };
            db.AddRange(campus, item, category); await db.SaveChangesAsync();
            var location = new InventoryLocation { TenantId = tenantId, CampusId = campus.Id,
                Code = "ACAP-L-" + suffix, Name = "Equipment store", IsActive = true };
            var supplier = new ProcurementSupplier { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), SupplierCode = "ACAP-S-" + suffix,
                Name = "Lab supplier", StateCode = "Approved" };
            var requisition = new ProcurementRequisition { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), RequestNumber = "ACAP-R-" + suffix,
                Purpose = "Serialized asset", StateCode = "Approved" };
            db.AddRange(location, supplier, requisition); await db.SaveChangesAsync();
            storeId = location.Id;
            var line = new ProcurementRequisitionLine { TenantId = tenantId, RequisitionId = requisition.Id,
                LineNo = 1, InventoryItemId = item.Id, Quantity = 2m, EstimatedUnitPrice = 12m };
            db.ProcurementRequisitionLines.Add(line); await db.SaveChangesAsync();
            var po = new ProcurementPurchaseOrder { TenantId = tenantId,
                ClientRequestId = Guid.NewGuid(), OrderNumber = "ACAP-PO-" + suffix,
                CampusId = campus.Id, SupplierId = supplier.Id, RequisitionId = requisition.Id,
                StateCode = "Approved", SubmittedAt = DateTime.UtcNow.AddMinutes(-10),
                DecidedAt = DateTime.UtcNow.AddMinutes(-1), DecidedByUserId = checkerId };
            db.ProcurementPurchaseOrders.Add(po); await db.SaveChangesAsync(); poId = po.Id;
            var poLine = new ProcurementPurchaseOrderLine { TenantId = tenantId,
                PurchaseOrderId = po.Id, RequisitionLineId = line.Id, LineNo = 1,
                InventoryItemId = item.Id, Quantity = 2m, UnitPrice = 12m };
            db.ProcurementPurchaseOrderLines.Add(poLine); await db.SaveChangesAsync(); poLineId = poLine.Id;
            var asset = new Asset { TenantId = tenantId, AssetCategoryId = category.Id,
                CampusId = campus.Id, InventoryItemId = item.Id, InventoryLocationId = location.Id,
                AssetTag = "ACAP-" + suffix, Name = "Centrifuge #" + suffix,
                SerialNumber = "SN-" + suffix, PurchaseCost = 12m,
                PurchaseDate = today, StateCode = "Available" };
            db.Assets.Add(asset);
            var period = new AccountingPeriod { TenantId = tenantId,
                Name = "Capitalization open " + suffix, StartDate = today.AddDays(-20),
                EndDate = today.AddMonths(2), IsClosed = false };
            db.AccountingPeriods.Add(period);
            var coa = new[]
            {
                ("Fixed", AccountType.Asset), ("Inventory", AccountType.Asset),
                ("Accumulated", AccountType.Asset), ("Depreciation", AccountType.Expense),
                ("Impairment", AccountType.Expense), ("Gain", AccountType.Income),
                ("Loss", AccountType.Expense)
            };
            var gl = coa.Select((x, i) => new Account { TenantId = tenantId,
                Code = "ACAP-" + i + "-" + suffix, Name = x.Item1,
                AccountType = x.Item2 }).ToArray();
            db.Accounts.AddRange(gl); await db.SaveChangesAsync();
            periodId = period.Id; assetReference = asset.PublicId; accounts = gl.Select(x => x.Id).ToArray();
        }
        await using (var db = Open(opts, tenantId, makerId))
        {
            var draft = await Goods(db, tenantId, makerId).CreateGoodsReceiptAsync(
                new CreateProcurementGoodsReceiptRequest
                {
                    ClientRequestId = Guid.NewGuid(), PurchaseOrderId = poId, LocationId = storeId,
                    Reason = "Accepted sealed serialized equipment for finance recognition",
                    Lines = [new ProcurementGoodsReceiptLineInput { PurchaseOrderLineId = poLineId,
                        Quantity = 2m, LotOrBatchNo = "LOT-" + suffix }]
                });
            Assert.True(draft.Success, draft.Message);
            receiptReference = draft.Data!.Reference;
            var submitted = await Goods(db, tenantId, makerId).SubmitGoodsReceiptAsync(
                receiptReference, new SubmitProcurementGoodsReceiptRequest
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = draft.Data.RowVersion
                });
            Assert.True(submitted.Success, submitted.Message);
        }
        await using (var db = Open(opts, tenantId, checkerId))
        {
            var receipt = (await Goods(db, tenantId, checkerId)
                .GetGoodsReceiptAsync(receiptReference)).Data!;
            var posted = await Goods(db, tenantId, checkerId).ReviewGoodsReceiptAsync(
                receiptReference, new ReviewProcurementGoodsReceiptRequest
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = receipt.RowVersion,
                    Decision = "Approve", ValuationMode = "Automatic",
                    Reason = "Independent serial and purchase-order goods inspection"
                });
            Assert.True(posted.Success, posted.Message);
        }
        var cap = new CreateAssetCapitalizationRequest
        {
            ClientRequestId = Guid.NewGuid(), ReceiptReference = receiptReference,
            ReceiptLineNo = 1, ReceiptUnitOrdinal = 1,
            AccountingPeriodId = periodId, CapitalizationDate = today,
            InServiceDate = today, PolicyMode = "Automatic", ProposedCostBasis = 0m,
            SalvageValue = 2m, DepreciationMethod = "StraightLine",
            UsefulLifeMonths = 36, PolicyVersion = 1,
            FixedAssetAccountId = accounts[0], InventoryAccountId = accounts[1],
            AccumulatedDepreciationAccountId = accounts[2],
            DepreciationExpenseAccountId = accounts[3],
            ImpairmentExpenseAccountId = accounts[4],
            DisposalGainAccountId = accounts[5], DisposalLossAccountId = accounts[6],
            Reason = "Receipt unit serial checked against fixed asset register and GL policy"
        };
        Guid command; string version;
        await using (var db = Open(opts, tenantId, makerId))
        {
            var finance = AssetFinance(db, tenantId, makerId);
            var readiness = await new EduOS.Service.Services.Inventory.AssetLifecycleService(
                db, new Actor(tenantId, makerId), TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EduOS.Service.Services.Inventory.AssetLifecycleService>.Instance)
                .GetCapitalizationReadinessAsync(assetReference);
            Assert.True(readiness.Success, readiness.Message);
            Assert.Equal(1, readiness.Data!.CandidateLineCount);
            cap.AssetRowVersion = readiness.Data.AssetRowVersion;
            var request = await finance.RequestCapitalizationAsync(assetReference, cap);
            Assert.True(request.Success, request.Message);
            command = request.Data!.Reference; version = request.Data.RowVersion;
            Assert.Equal("Submitted", request.Data.State);
            Assert.Equal(12m, request.Data.ProposedCostBasis);
            Assert.Equal(command, (await finance.RequestCapitalizationAsync(assetReference, cap)).Data!.Reference);
            Assert.Equal(403, (await finance.ReviewCapitalizationAsync(command,
                new ReviewAssetCapitalizationRequest { RowVersion = version, Approve = true,
                    Reason = "Same maker must never approve financial posting" })).StatusCode);
        }
        await using (var db = Open(opts, tenantId, checkerId))
        {
            var result = await AssetFinance(db, tenantId, checkerId).ReviewCapitalizationAsync(
                command, new ReviewAssetCapitalizationRequest
                {
                    RowVersion = version, Approve = true,
                    Reason = "Independent GL capitalization and stock-issue approval"
                });
            Assert.True(result.Success, result.Message);
            Assert.Equal("Posted", result.Data!.State);
            Assert.Equal(12m, result.Data.PostedStockCost);
            Assert.Single(await db.AssetBooks.Where(x => !x.IsDeleted).ToListAsync());
            var issued = Assert.Single(await db.InventoryMovements.Where(x =>
                x.SourceType == "AssetCapitalization" && x.StateCode == "Posted").ToListAsync());
            Assert.Equal("Issue", issued.MovementTypeCode);
            var journal = Assert.Single(await db.Journals.Where(x =>
                x.SourceType == "AssetCapitalization" && x.State == JournalState.Posted).ToListAsync());
            var journalLines = await db.JournalLines.Where(x => x.JournalId == journal.Id).ToListAsync();
            Assert.Equal(12m, journalLines.Sum(x => x.DebitAmount));
            Assert.Equal(12m, journalLines.Sum(x => x.CreditAmount));
            Assert.Equal(journal.Id, (await db.AssetCapitalizationCommands.SingleAsync()).JournalId);
            Assert.Equal(409, (await AssetFinance(db, tenantId, checkerId).ReviewCapitalizationAsync(
                command, new ReviewAssetCapitalizationRequest { RowVersion = version,
                    Approve = true, Reason = "Duplicate GL posting must conflict" })).StatusCode);
        }
        Guid unwindReference;
        string unwindVersion;
        AssetReversalRequest capitalizationReversalCommand;
        await using (var db = Open(opts, tenantId, makerId))
        {
            var finance = AssetFinance(db, tenantId, makerId);
            var posted = (await finance.GetCapitalizationAsync(assetReference)).Data!;
            capitalizationReversalCommand = new AssetReversalRequest
            {
                ClientRequestId = Guid.NewGuid(), OriginalRowVersion = posted.RowVersion,
                AccountingPeriodId = periodId, ReversalDate = today,
                Reason = "Serialized asset not commissioned, unwind both stock and original GL"
            };
            // Two distinct maker keys race for the SAME posted serialized unit.
            // Only one source-linked submitted contra may survive; the losing key
            // must not leave an orphan Finance journal or reverse any stock.
            var competingReversalCommand = new AssetReversalRequest
            {
                ClientRequestId = Guid.NewGuid(),
                OriginalRowVersion = capitalizationReversalCommand.OriginalRowVersion,
                AccountingPeriodId = periodId, ReversalDate = today,
                Reason = "Competing serialized-unit correction must not duplicate stock or GL"
            };
            var pendingCommands = new[] { capitalizationReversalCommand, competingReversalCommand };
            var pendingAttempts = await Task.WhenAll(pendingCommands.Select(async candidate =>
            {
                await using var contender = Open(opts, tenantId, makerId);
                return await AssetFinance(contender, tenantId, makerId)
                    .RequestCapitalizationReversalAsync(command, candidate);
            }));
            var acceptedPending = Assert.Single(pendingAttempts, x => x.Success);
            Assert.Single(pendingAttempts, x => x.StatusCode == 409);
            var losingCommand = pendingAttempts[0].Success
                ? competingReversalCommand : capitalizationReversalCommand;
            capitalizationReversalCommand = pendingAttempts[0].Success
                ? capitalizationReversalCommand : competingReversalCommand;
            Assert.Equal("Posted", acceptedPending.Data!.State);
            unwindReference = acceptedPending.Data.PendingReversalReference!.Value;
            unwindVersion = acceptedPending.Data.PendingReversalRowVersion!;
            Assert.Null(acceptedPending.Data.FinanceReversalReference);
            await using (var pendingAudit = Open(opts, tenantId, makerId))
            {
                var onlyPending = Assert.Single(await pendingAudit.Journals.AsNoTracking()
                    .Where(x => x.SourceType == "AssetCapitalizationReversal").ToListAsync());
                Assert.Equal(JournalState.Submitted, onlyPending.State);
                Assert.Equal(capitalizationReversalCommand.ClientRequestId, onlyPending.IdempotencyKey);
                Assert.False(await pendingAudit.Journals.AsNoTracking()
                    .AnyAsync(x => x.IdempotencyKey == losingCommand.ClientRequestId));
                Assert.Empty(await pendingAudit.InventoryMovements.AsNoTracking()
                    .Where(x => x.ReversalOfMovementId != null).ToListAsync());
            }
            var pendingReplay = await finance.RequestCapitalizationReversalAsync(
                command, capitalizationReversalCommand);
            Assert.True(pendingReplay.Success, pendingReplay.Message);
            Assert.Equal(unwindReference, pendingReplay.Data!.PendingReversalReference);
        }
        await using (var staleDb = Open(opts, tenantId, checkerId))
        {
            var stale = await AssetFinance(staleDb, tenantId, checkerId)
                .ReviewCapitalizationReversalAsync(command, unwindReference,
                    new AssetReviewRequest { RowVersion = "invalid", Approve = true,
                        Reason = "Stale review must not mutate stock or GL" });
            Assert.Equal(409, stale.StatusCode);
        }
        // Independent SQL contexts compete to post the exact same approved
        // stock reversal and Finance contra. A rejected contender is not a
        // second posting, even when both started with the same rowversion.
        var reviewAttempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var contender = Open(opts, tenantId, checkerId);
            return await AssetFinance(contender, tenantId, checkerId)
                .ReviewCapitalizationReversalAsync(command, unwindReference,
                    new AssetReviewRequest { RowVersion = unwindVersion, Approve = true,
                        Reason = "Independent physical unit return and GL contra approval" });
        }));
        var unwound = Assert.Single(reviewAttempts, x => x.Success);
        Assert.Single(reviewAttempts, x => x.StatusCode == 409);
        Assert.Equal("Reversed", unwound.Data!.State);
        await using (var db = Open(opts, tenantId, checkerId))
        {
            var finance = AssetFinance(db, tenantId, checkerId);
            Assert.NotNull(unwound.Data.FinanceReversalReference);
            Assert.NotNull(unwound.Data.StockReversalReference);
            Assert.False((await db.AssetBooks.SingleAsync()).IsActive);
            Assert.Single(await db.InventoryMovements.Where(x =>
                x.ReversalOfMovementId != null && x.StateCode == "Posted").ToListAsync());
            var journals = await db.Journals.Where(x => x.State == JournalState.Posted &&
                (x.SourceType == "AssetCapitalization" ||
                    x.SourceType == "AssetCapitalizationReversal")).ToListAsync();
            Assert.Equal(2, journals.Count);
            var originalJournal = Assert.Single(journals.Where(x =>
                x.SourceType == "AssetCapitalization"));
            var correctionJournal = Assert.Single(journals.Where(x =>
                x.SourceType == "AssetCapitalizationReversal"));
            Assert.Equal(originalJournal.Id, correctionJournal.ReversalOfJournalId);
            var contraLines = await db.JournalLines.AsNoTracking()
                .Where(x => x.JournalId == correctionJournal.Id).ToListAsync();
            Assert.Equal(2, contraLines.Count);
            Assert.Equal(12m, contraLines.Sum(x => x.DebitAmount));
            Assert.Equal(12m, contraLines.Sum(x => x.CreditAmount));
            Assert.Equal(409, (await finance.ReviewCapitalizationReversalAsync(command,
                unwindReference, new AssetReviewRequest { RowVersion = unwindVersion,
                    Approve = true, Reason = "Duplicate unwind must not double issue" })).StatusCode);
            Assert.Equal(409, (await finance.RequestCapitalizationReversalAsync(
                command, capitalizationReversalCommand)).StatusCode);
        }
        await using (var db = Open(opts, tenantId, makerId))
        {
            var finance = AssetFinance(db, tenantId, makerId);
            var replay = await finance.RequestCapitalizationReversalAsync(
                command, capitalizationReversalCommand);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(unwindReference, replay.Data!.FinanceReversalReference);
            Assert.Equal(409, (await finance.RequestCapitalizationReversalAsync(
                command, new AssetReversalRequest
                {
                    ClientRequestId = capitalizationReversalCommand.ClientRequestId,
                    OriginalRowVersion = capitalizationReversalCommand.OriginalRowVersion,
                    AccountingPeriodId = capitalizationReversalCommand.AccountingPeriodId,
                    ReversalDate = capitalizationReversalCommand.ReversalDate,
                    Reason = "Changed reversal reason must not reuse the same command key"
                })).StatusCode);
            Assert.Single(await db.Journals.AsNoTracking().Where(x =>
                x.SourceType == "AssetCapitalizationReversal" &&
                x.PublicId == unwindReference && x.State == JournalState.Posted).ToListAsync());
        }
    }
}
