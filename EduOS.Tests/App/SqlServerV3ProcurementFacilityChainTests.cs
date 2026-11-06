using EduOS.Core.DTOs.Facilities;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Enterprise;
using EduOS.Service.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3ProcurementPurchaseOrderTests
{
    // Continues the same disposable SQL PO, approved GR, supplier AP/credit,
    // serialized asset and Finance fixture. No synthetic stock or journal rows
    // are inserted: the two canonical services own all further postings.
    private async Task VerifyPostedProcurementAssetFacilityChainAsync(
        DbContextOptions<EduOSDbContext> options, long tenantId, long makerId,
        long checkerId, long poId, long locationId, long periodId,
        long inventoryAccountId, long payableAccountId, long varianceAccountId,
        Guid receiptReference, Guid invoiceReference,
        Guid assetReference, DateOnly accountingDate, string suffix)
    {
        long campusId, itemId, requestId, originalMovementId;
        await using (var check = Open(options, tenantId, makerId))
        {
            var po = await check.ProcurementPurchaseOrders.AsNoTracking()
                .SingleAsync(p => p.Id == poId);
            var receipt = await check.ProcurementGoodsReceipts.AsNoTracking()
                .SingleAsync(r => r.PublicId == receiptReference);
            Assert.Equal("Approved", po.StateCode);
            Assert.Equal(po.Id, receipt.PurchaseOrderId);
            Assert.Equal(po.CampusId, receipt.CampusId);
            Assert.Equal("Posted", receipt.StateCode);
            Assert.NotNull(receipt.PostedMovementId);
            campusId = receipt.CampusId;
            var receiptLine = await check.ProcurementGoodsReceiptLines.AsNoTracking()
                .SingleAsync(l => l.GoodsReceiptId == receipt.Id);
            itemId = receiptLine.InventoryItemId;
            Assert.Equal(10m, receiptLine.PostedUnitCost!.Value);
            var stock = await check.InventoryMovements.AsNoTracking()
                .SingleAsync(m => m.Id == receipt.PostedMovementId);
            Assert.Equal("ProcurementGoodsReceipt", stock.SourceType);
            Assert.Equal(receipt.Id, stock.SourceId);
            Assert.Equal("Posted", stock.StateCode);
            originalMovementId = stock.Id;
            var invoice = await check.ProcurementSupplierInvoices.AsNoTracking()
                .SingleAsync(i => i.PublicId == invoiceReference);
            Assert.Equal(po.Id, invoice.PurchaseOrderId);
            Assert.Equal("Posted", invoice.StateCode);
            var invoiceLine = await check.ProcurementSupplierInvoiceLines.AsNoTracking()
                .SingleAsync(l => l.SupplierInvoiceId == invoice.Id);
            Assert.Equal(receiptLine.Id, invoiceLine.GoodsReceiptLineId);
            Assert.Equal(itemId, invoiceLine.InventoryItemId);
            Assert.Equal(10m, invoiceLine.StockUnitCostSnapshot);
            var assetId = await check.Assets.AsNoTracking()
                .Where(a => a.PublicId == assetReference).Select(a => a.Id).SingleAsync();
            var capitalization = await check.AssetCapitalizationCommands.AsNoTracking()
                .SingleAsync(c => c.AssetId == assetId && c.StateCode == "Posted");
            Assert.Equal(receiptLine.Id, capitalization.GoodsReceiptLineId);
            Assert.Equal(1, capitalization.ReceiptUnitOrdinal);
            Assert.NotNull(capitalization.StockMovementId);
            Assert.NotNull(capitalization.JournalId);
            Assert.NotNull(capitalization.AssetBookId);
            var book = await check.AssetBooks.AsNoTracking()
                .SingleAsync(b => b.Id == capitalization.AssetBookId);
            Assert.True(book.IsActive); // Previous disposal has an approved contra.
            Assert.Equal(10m, book.CostBasis);
            var assetStock = await check.InventoryMovements.AsNoTracking()
                .SingleAsync(m => m.Id == capitalization.StockMovementId);
            Assert.Equal("AssetCapitalization", assetStock.SourceType);
            Assert.Equal("Posted", assetStock.StateCode);
            Assert.Equal("Issue", assetStock.MovementTypeCode);
            var assetJournal = await check.Journals.AsNoTracking()
                .SingleAsync(j => j.Id == capitalization.JournalId);
            Assert.Equal("AssetCapitalization", assetJournal.SourceType);
            Assert.Equal(JournalState.Posted, assetJournal.State);
            var assetJournalLines = await check.JournalLines.AsNoTracking()
                .Where(l => l.JournalId == assetJournal.Id).ToListAsync();
            Assert.Equal(10m, assetJournalLines.Sum(l => l.DebitAmount));
            Assert.Equal(10m, assetJournalLines.Sum(l => l.CreditAmount));
            var balance = await Inventory(check, makerId)
                .GetItemBalanceAsync(itemId, locationId, null);
            Assert.True(balance.Success, balance.Message);
            Assert.Equal(2m, balance.Data!.QuantityOnHand); // GR 4 - supplier return 1 - asset 1.
        }

        // The earlier invoice reversal released two accepted but uninvoiced
        // receipt units. Approve the replacement bill before comparing total
        // inventory GL value with physical stock; without it the difference is
        // a genuine unbilled-GR reconciliation exception, not an arithmetic fix.
        var replacementBill = new CreateProcurementSupplierInvoiceRequest {
            ClientRequestId = Guid.NewGuid(), PurchaseOrderId = poId,
            SupplierInvoiceNumber = "CHAIN-INV-" + suffix,
            InvoiceDate = accountingDate, DueDate = accountingDate.AddDays(15),
            PriceMode = "Automatic", Reason = "Recognize two previously unbilled accepted units",
            Lines = [new PreviewProcurementInvoiceLineRequest {
                GoodsReceiptReference = receiptReference, ReceiptLineNo = 1, Quantity = 2m }] };
        Guid replacementReference;
        string replacementVersion;
        await using (var maker = Open(options, tenantId, makerId))
        {
            var invoice = Invoices(maker, tenantId, makerId);
            var draft = await invoice.CreateAsync(replacementBill);
            Assert.True(draft.Success, draft.Message);
            replacementReference = draft.Data!.Reference;
            var submitted = await invoice.SubmitAsync(replacementReference,
                new SubmitProcurementSupplierInvoiceRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = draft.Data.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
            replacementVersion = submitted.Data!.RowVersion;
        }
        await using (var reviewer = Open(options, tenantId, checkerId))
        {
            var approval = await Invoices(reviewer, tenantId, checkerId)
                .ReviewAsync(replacementReference, new ReviewProcurementSupplierInvoiceRequest {
                    ClientRequestId = Guid.NewGuid(), RowVersion = replacementVersion,
                    Decision = "Approve", AccountingPeriodId = periodId,
                    InventoryAccountId = inventoryAccountId, PayableAccountId = payableAccountId,
                    PriceVarianceAccountId = varianceAccountId,
                    Reason = "Independent posting of the residual received supplier stock" });
            Assert.True(approval.Success, approval.Message);
            Assert.Equal("Posted", approval.Data!.StateCode);
            Assert.Equal(20m, approval.Data.TotalAmount);
            Assert.Equal(20m, approval.Data.StockValue);
            var journal = await reviewer.Journals.AsNoTracking()
                .SingleAsync(j => j.PublicId == approval.Data.JournalReference);
            Assert.Equal(JournalState.Posted, journal.State);
            Assert.Equal("ProcurementSupplierInvoice", journal.SourceType);
            Assert.Equal(20m, await reviewer.JournalLines.AsNoTracking()
                .Where(l => l.JournalId == journal.Id && l.AccountId == inventoryAccountId)
                .SumAsync(l => l.DebitAmount));
        }

        long expenseAccountId;
        await using (var setup = Open(options, tenantId, makerId))
        {
            var space = new FacilitySpace { TenantId = tenantId, CampusId = campusId,
                Code = "CHAIN-SPACE-" + suffix, Name = "Receipt-linked maintenance" };
            var repairs = new Account { TenantId = tenantId, Code = "CHAIN-EXP-" + suffix,
                Name = "Facility maintenance expense", AccountType = AccountType.Expense,
                IsActive = true, AllowsPosting = true };
            setup.AddRange(space, repairs);
            await setup.SaveChangesAsync();
            expenseAccountId = repairs.Id;
            var request = new FacilityServiceRequest { TenantId = tenantId,
                ClientRequestId = Guid.NewGuid(), CampusId = campusId,
                FacilitySpaceId = space.Id, RequesterUserId = makerId,
                CategoryCode = "Repair", PriorityCode = "Normal",
                Summary = "Install the remaining supplied equipment as a maintenance spare",
                StateCode = "Approved", ApprovedByUserId = checkerId,
                ApprovedAtUtc = DateTime.UtcNow };
            setup.FacilityServiceRequests.Add(request);
            await setup.SaveChangesAsync();
            requestId = request.Id;
            setup.FacilityWorkOrders.Add(new FacilityWorkOrder { TenantId = tenantId,
                FacilityServiceRequestId = request.Id, StateCode = "InProgress" });
            await setup.SaveChangesAsync();
        }

        InventoryMovementService Inventory(EduOSDbContext db, long actorId) =>
            new(db, new Actor(tenantId, actorId), TimeProvider.System,
                NullLogger<InventoryMovementService>.Instance);
        FacilityPartsFinanceService Finance(EduOSDbContext db, long actorId) =>
            new(db, new Actor(tenantId, actorId), TimeProvider.System,
                Inventory(db, actorId), NullLogger<FacilityPartsFinanceService>.Instance);

        var issueInput = new CreateFacilityPartsIssueDto { ClientRequestId = Guid.NewGuid(),
            FromLocationId = locationId, Reason = "Consume one receipt-linked spare under approved maintenance",
            Lines = [new FacilityPartsIssueLineInputDto {
                InventoryItemId = itemId, Quantity = 1m, LotOrBatchNo = "LOT-SI" }] };
        Guid issueReference;
        string issueVersion;
        await using (var unauthorized = Open(options, tenantId, makerId))
            Assert.Equal(403, (await new InventoryMovementService(unauthorized,
                new ReadOnlyActor(tenantId, makerId), TimeProvider.System,
                NullLogger<InventoryMovementService>.Instance)
                .CreateFacilityPartsIssueAsync(requestId, issueInput)).StatusCode);
        await using (var maker = Open(options, tenantId, makerId))
        {
            var service = Inventory(maker, makerId);
            var draft = await service.CreateFacilityPartsIssueAsync(requestId, issueInput);
            Assert.True(draft.Success, draft.Message);
            Assert.Equal("FacilityWorkOrder", draft.Data!.SourceType);
            issueReference = draft.Data.Reference;
            issueVersion = draft.Data.RowVersion;
            Assert.Equal(issueReference, (await service.CreateFacilityPartsIssueAsync(
                requestId, issueInput)).Data!.Reference);
            Assert.Equal(409, (await service.PostMovementAsync(issueReference,
                new PostInventoryMovementRequestDto { RowVersion = issueVersion })).StatusCode);
        }
        // Scope each HTTP-bound DbContext inside its own execution flow. The
        // shared AsyncLocal accessor must not mix synthetic tenant requests.
        var competingPost = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = Open(options, tenantId, checkerId);
            return await Inventory(db, checkerId).PostMovementAsync(issueReference,
                new PostInventoryMovementRequestDto { RowVersion = issueVersion });
        })));
        Assert.Contains(competingPost, x => x.Success);
        Assert.All(competingPost, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));

        long issueId;
        await using (var check = Open(options, tenantId, makerId))
        {
            var movement = await check.InventoryMovements.AsNoTracking()
                .SingleAsync(m => m.PublicId == issueReference);
            var orderId = await check.FacilityWorkOrders.AsNoTracking()
                .Where(w => w.FacilityServiceRequestId == requestId).Select(w => w.Id).SingleAsync();
            Assert.Equal(orderId, movement.SourceId);
            Assert.Equal("Posted", movement.StateCode);
            issueId = movement.Id;
            var line = await check.InventoryMovementLines.AsNoTracking()
                .SingleAsync(l => l.InventoryMovementId == movement.Id);
            Assert.Equal(itemId, line.InventoryItemId);
            Assert.Equal(1m, line.Quantity);
            Assert.Equal(10m, line.UnitCost);
            Assert.Equal(1m, (await Inventory(check, makerId)
                .GetItemBalanceAsync(itemId, locationId, null)).Data!.QuantityOnHand);
            var trace = await new V3EnterpriseLedgerTraceService(
                check, new Actor(tenantId, makerId)).GetAsync(issueReference);
            Assert.True(trace.Success, trace.Message);
            Assert.Equal("FacilityWorkOrder", trace.Data!.SourceType);
            Assert.Equal("VerifiedFacilityWorkOrder", trace.Data.SourceEvidenceStatus);
            Assert.Equal("NoExplicitReference", trace.Data.FinanceEvidenceStatus);
            Assert.Equal(10m, trace.Data.RecordedMovementValue);
            Assert.Equal(200, (await new V3EnterpriseLedgerTraceService(
                check, new Actor(tenantId, makerId)).GetAsync(
                (await check.InventoryMovements.AsNoTracking()
                    .Where(m => m.Id == originalMovementId).Select(m => m.PublicId).SingleAsync()))).StatusCode);
        }

        var financeInput = new FacilityPartsFinanceRequestDto { ClientRequestId = Guid.NewGuid(),
            AccountingPeriodId = periodId, JournalDate = accountingDate,
            RepairsExpenseAccountId = expenseAccountId,
            InventoryAssetAccountId = inventoryAccountId,
            Reason = "Source-linked expense for the approved maintenance consumption" };
        Guid journalReference;
        string financeVersion;
        await using (var maker = Open(options, tenantId, makerId))
        {
            var finance = Finance(maker, makerId);
            var submitted = await finance.RequestAsync(issueReference, financeInput);
            Assert.True(submitted.Success, submitted.Message);
            Assert.Equal("Submitted", submitted.Data!.State);
            Assert.Equal(10m, submitted.Data.AmountBdt);
            journalReference = submitted.Data.JournalReference;
            financeVersion = submitted.Data.RowVersion;
            Assert.Equal(journalReference,
                (await finance.RequestAsync(issueReference, financeInput)).Data!.JournalReference);
            Assert.Equal(403, (await finance.ReviewAsync(journalReference,
                new FacilityPartsFinanceReviewDto { RowVersion = financeVersion,
                    Approve = true })).StatusCode);
        }
        await using (var locked = Open(options, tenantId, checkerId))
        {
            await locked.AccountingPeriods.Where(p => p.Id == periodId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsClosed, true));
            var denied = await Finance(locked, checkerId).ReviewAsync(journalReference,
                new FacilityPartsFinanceReviewDto { RowVersion = financeVersion, Approve = true });
            Assert.Equal(409, denied.StatusCode);
            Assert.Equal(JournalState.Submitted, await locked.Journals.AsNoTracking()
                .Where(j => j.PublicId == journalReference).Select(j => j.State).SingleAsync());
            await locked.AccountingPeriods.Where(p => p.Id == periodId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsClosed, false));
        }
        await using (var reviewer = Open(options, tenantId, checkerId))
        {
            var posted = await Finance(reviewer, checkerId).ReviewAsync(journalReference,
                new FacilityPartsFinanceReviewDto { RowVersion = financeVersion, Approve = true });
            Assert.True(posted.Success, posted.Message);
            Assert.Equal("Posted", posted.Data!.State);
            var journal = await reviewer.Journals.AsNoTracking()
                .SingleAsync(j => j.PublicId == journalReference);
            Assert.Equal("InventoryMovement", journal.SourceType);
            Assert.Equal(issueId, journal.SourceId);
            Assert.Equal(campusId, journal.CampusId);
            Assert.Equal(JournalState.Posted, journal.State);
            var lines = await reviewer.JournalLines.AsNoTracking()
                .Where(l => l.JournalId == journal.Id).OrderBy(l => l.LineNo).ToListAsync();
            Assert.Equal(2, lines.Count);
            Assert.Equal((expenseAccountId, 10m, 0m),
                (lines[0].AccountId, lines[0].DebitAmount, lines[0].CreditAmount));
            Assert.Equal((inventoryAccountId, 0m, 10m),
                (lines[1].AccountId, lines[1].DebitAmount, lines[1].CreditAmount));
            var trace = await new V3EnterpriseLedgerTraceService(
                reviewer, new Actor(tenantId, checkerId)).GetAsync(issueReference);
            Assert.True(trace.Success, trace.Message);
            Assert.Equal("VerifiedFacilityWorkOrder", trace.Data!.SourceEvidenceStatus);
            Assert.Equal("ExplicitBalancedReference", trace.Data!.FinanceEvidenceStatus);
            Assert.Equal(journalReference, Assert.Single(trace.Data.Journals).Reference);
        }
        await using (var foreign = Open(options, tenantId + 999999, checkerId))
            Assert.Equal(404, (await new FacilityPartsFinanceService(foreign,
                new Actor(tenantId + 999999, checkerId), TimeProvider.System,
                new InventoryMovementService(foreign, new Actor(tenantId + 999999, checkerId),
                    TimeProvider.System, NullLogger<InventoryMovementService>.Instance),
                NullLogger<FacilityPartsFinanceService>.Instance).GetAsync(issueReference)).StatusCode);

        // A privileged historical GL corruption must show as a reconciliation
        // conflict. Rollback restores the immutable source without stock writes.
        await using (var probe = Open(options, tenantId, makerId))
        {
            var original = await probe.Journals.AsNoTracking()
                .SingleAsync(j => j.PublicId == journalReference);
            await using (var balancedDrift = await probe.Database.BeginTransactionAsync())
            {
                // Still balanced 11/11, but source stock cost is 10.
                await probe.JournalLines.Where(l => l.JournalId == original.Id && l.LineNo == 1)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.DebitAmount, 11m));
                await probe.JournalLines.Where(l => l.JournalId == original.Id && l.LineNo == 2)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreditAmount, 11m));
                var wrongValue = await new V3EnterpriseLedgerTraceService(
                    probe, new Actor(tenantId, makerId)).GetAsync(issueReference);
                Assert.True(wrongValue.Success, wrongValue.Message);
                Assert.Equal("JournalAmountMismatch", wrongValue.Data!.FinanceEvidenceStatus);
                Assert.All(wrongValue.Data.Journals, j => Assert.True(j.Balanced));
                var register = await new V3EnterpriseReconciliationRegisterService(
                    probe, new Actor(tenantId, makerId))
                    .GetAsync("stock", accountingDate, accountingDate, campusId, 1, 50);
                Assert.True(register.Success, register.Message);
                Assert.Contains(register.Data!.Items, row => row.Reference == issueReference &&
                    row.EvidenceStatus == "JournalAmountMismatch");
                await balancedDrift.RollbackAsync();
            }
            await using (var rejectedHistory = await probe.Database.BeginTransactionAsync())
            {
                // A rejected older attempt is auditable, not a second active GL posting.
                var history = new Journal { TenantId = tenantId,
                    JournalNumber = "FPI-REJ-" + Guid.NewGuid().ToString("N"),
                    JournalDate = accountingDate, AccountingPeriodId = periodId,
                    CampusId = campusId, SourceType = "InventoryMovement", SourceId = issueId,
                    State = JournalState.Rejected, CreatedBy = makerId };
                probe.Journals.Add(history);
                await probe.SaveChangesAsync();
                probe.JournalLines.AddRange(
                    new JournalLine { TenantId = tenantId, JournalId = history.Id, LineNo = 1,
                        CampusId = campusId, AccountId = expenseAccountId, DebitAmount = 10m },
                    new JournalLine { TenantId = tenantId, JournalId = history.Id, LineNo = 2,
                        CampusId = campusId, AccountId = inventoryAccountId, CreditAmount = 10m });
                await probe.SaveChangesAsync();
                var trace = await new V3EnterpriseLedgerTraceService(
                    probe, new Actor(tenantId, makerId)).GetAsync(issueReference);
                Assert.True(trace.Success, trace.Message);
                Assert.Equal(2, trace.Data!.Journals.Count);
                Assert.Equal("ExplicitBalancedReference", trace.Data.FinanceEvidenceStatus);
                var register = await new V3EnterpriseReconciliationRegisterService(
                    probe, new Actor(tenantId, makerId))
                    .GetAsync("stock", accountingDate, accountingDate, campusId, 1, 50);
                Assert.True(register.Success, register.Message);
                Assert.Contains(register.Data!.Items, row => row.Reference == issueReference &&
                    row.EvidenceStatus == "ExplicitBalancedReference");
                await rejectedHistory.RollbackAsync();
                probe.ChangeTracker.Clear();
            }
            await using (var tx = await probe.Database.BeginTransactionAsync())
            {
                await probe.JournalLines.Where(l => l.JournalId == original.Id && l.LineNo == 2)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreditAmount, 11m));
                var tampered = await new V3EnterpriseLedgerTraceService(
                    probe, new Actor(tenantId, makerId)).GetAsync(issueReference);
                Assert.True(tampered.Success, tampered.Message);
                Assert.Equal("JournalImbalance", tampered.Data!.FinanceEvidenceStatus);
                await tx.RollbackAsync();
            }
            await using (var orphaned = await probe.Database.BeginTransactionAsync())
            {
                var orderId = await probe.InventoryMovements.AsNoTracking()
                    .Where(m => m.Id == issueId).Select(m => m.SourceId!.Value).SingleAsync();
                await probe.FacilityWorkOrders.Where(w => w.Id == orderId)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDeleted, true));
                Assert.Equal(409, (await new V3EnterpriseLedgerTraceService(
                    probe, new Actor(tenantId, makerId)).GetAsync(issueReference)).StatusCode);
                var register = await new V3EnterpriseReconciliationRegisterService(
                    probe, new Actor(tenantId, makerId))
                    .GetAsync("stock", accountingDate, accountingDate, campusId, 1, 50);
                Assert.True(register.Success, register.Message);
                Assert.Contains(register.Data!.Items, row => row.Reference == issueReference &&
                    row.EvidenceStatus == "EvidenceConflict");
                await orphaned.RollbackAsync();
            }
        }
        await using (var locked = Open(options, tenantId, makerId))
        {
            await locked.AccountingPeriods.Where(p => p.Id == periodId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsClosed, true));
            Assert.Equal(409, (await Finance(locked, makerId).RequestReversalAsync(
                journalReference, new FacilityPartsFinanceReversalRequestDto {
                    ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
                    JournalDate = accountingDate,
                    Reason = "Closed period cannot reverse stock or posted Finance" })).StatusCode);
            Assert.False(await locked.Journals.AsNoTracking().AnyAsync(j =>
                j.SourceType == "FacilityPartsExpenseReversal"));
            Assert.False(await locked.InventoryMovements.AsNoTracking().AnyAsync(m =>
                m.ReversalOfMovementId == issueId && m.StateCode == "Posted"));
            await locked.AccountingPeriods.Where(p => p.Id == periodId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsClosed, false));
        }

        Guid reversalReference;
        string reversalVersion;
        var reversalRequestId = Guid.NewGuid();
        await using (var maker = Open(options, tenantId, makerId))
        {
            var finance = Finance(maker, makerId);
            var input = new FacilityPartsFinanceReversalRequestDto {
                ClientRequestId = reversalRequestId,
                AccountingPeriodId = periodId, JournalDate = accountingDate,
                Reason = "Return receipt-linked unused parts with exact Finance compensation" };
            var reversal = await finance.RequestReversalAsync(journalReference, input);
            Assert.True(reversal.Success, reversal.Message);
            reversalReference = reversal.Data!.FinanceReversalReference!.Value;
            reversalVersion = reversal.Data.ReversalRowVersion!;
            var replay = await finance.RequestReversalAsync(journalReference, input);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(reversalReference, replay.Data!.FinanceReversalReference);
            var distinctKey = await finance.RequestReversalAsync(journalReference,
                new FacilityPartsFinanceReversalRequestDto { ClientRequestId = Guid.NewGuid(),
                    AccountingPeriodId = periodId, JournalDate = accountingDate,
                    Reason = input.Reason });
            Assert.Equal(409, distinctKey.StatusCode);
        }
        await using (var foreign = Open(options, tenantId + 999999, checkerId))
        {
            var foreignActor = new Actor(tenantId + 999999, checkerId);
            var inventory = new InventoryMovementService(foreign, foreignActor,
                TimeProvider.System, NullLogger<InventoryMovementService>.Instance);
            var finance = new FacilityPartsFinanceService(foreign, foreignActor,
                TimeProvider.System, inventory, NullLogger<FacilityPartsFinanceService>.Instance);
            Assert.Equal(404, (await finance.ReviewReversalAsync(reversalReference,
                new FacilityPartsFinanceReviewDto { RowVersion = reversalVersion,
                    Approve = true })).StatusCode);
        }
        await using (var pending = Open(options, tenantId, checkerId))
        {
            var trace = await new V3EnterpriseLedgerTraceService(
                pending, new Actor(tenantId, checkerId)).GetAsync(issueReference);
            Assert.True(trace.Success, trace.Message);
            Assert.Equal("ReversalPending", trace.Data!.FinanceEvidenceStatus);
            Assert.Null(trace.Data.ReversalReference);
            Assert.Equal(reversalReference, trace.Data.FinanceReversalReference);
            var register = await new V3EnterpriseReconciliationRegisterService(
                pending, new Actor(tenantId, checkerId))
                .GetAsync("stock", accountingDate, accountingDate, campusId, 1, 50);
            Assert.True(register.Success, register.Message);
            Assert.Contains(register.Data!.Items, x => x.Reference == issueReference &&
                x.EvidenceStatus == "ReversalPending");
        }
        var competingReversal = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var reviewer = Open(options, tenantId, checkerId);
            return await Finance(reviewer, checkerId).ReviewReversalAsync(reversalReference,
                new FacilityPartsFinanceReviewDto { RowVersion = reversalVersion, Approve = true });
        }));
        Assert.Contains(competingReversal, x => x.Success);
        Assert.All(competingReversal, x =>
            Assert.True(x.Success || x.StatusCode == 409, x.Message));
        await using (var reviewer = Open(options, tenantId, checkerId))
        {
            var reversed = await Finance(reviewer, checkerId).ReviewReversalAsync(reversalReference,
                new FacilityPartsFinanceReviewDto { RowVersion = reversalVersion, Approve = true });
            Assert.True(reversed.Success, reversed.Message);
            Assert.Equal("Reversed", reversed.Data!.State);
            Assert.NotNull(reversed.Data.StockReversalReference);
            var original = await reviewer.Journals.AsNoTracking()
                .SingleAsync(j => j.PublicId == journalReference);
            var contra = await reviewer.Journals.AsNoTracking()
                .SingleAsync(j => j.PublicId == reversalReference);
            Assert.Equal(original.Id, contra.ReversalOfJournalId);
            Assert.Equal(JournalState.Posted, contra.State);
            var oldLines = await reviewer.JournalLines.AsNoTracking()
                .Where(l => l.JournalId == original.Id).OrderBy(l => l.LineNo).ToListAsync();
            var inverse = await reviewer.JournalLines.AsNoTracking()
                .Where(l => l.JournalId == contra.Id).OrderBy(l => l.LineNo).ToListAsync();
            Assert.Equal(oldLines.Count, inverse.Count);
            for (var i = 0; i < oldLines.Count; i++)
            {
                Assert.Equal(oldLines[i].AccountId, inverse[i].AccountId);
                Assert.Equal(oldLines[i].DebitAmount, inverse[i].CreditAmount);
                Assert.Equal(oldLines[i].CreditAmount, inverse[i].DebitAmount);
            }
            var stockContra = await reviewer.InventoryMovements.AsNoTracking()
                .SingleAsync(m => m.PublicId == reversed.Data.StockReversalReference);
            Assert.Equal(issueId, stockContra.ReversalOfMovementId);
            Assert.Equal("Posted", stockContra.StateCode);
            Assert.Equal(2m, (await Inventory(reviewer, checkerId)
                .GetItemBalanceAsync(itemId, locationId, null)).Data!.QuantityOnHand);
            var outstanding = (await Invoices(reviewer, tenantId, checkerId)
                .AgingAsync(accountingDate, null, false, 1, 100)).Data!;
            Assert.Equal(9m, Assert.Single(outstanding.Items,
                x => x.InvoiceReference == invoiceReference).SourceLinkedOutstandingAmount);
            Assert.Equal(1, await reviewer.AssetBooks.CountAsync(b => b.IsActive));
            Assert.Equal(1, await reviewer.Journals.CountAsync(j =>
                j.SourceType == "InventoryMovement" && j.SourceId == issueId && j.State == JournalState.Posted));
            Assert.Equal(1, await reviewer.Journals.CountAsync(j =>
                j.SourceType == "FacilityPartsExpenseReversal" &&
                j.ReversalOfJournalId == original.Id && j.State == JournalState.Posted));
            Assert.Equal(1, await reviewer.InventoryMovements.CountAsync(m =>
                m.ReversalOfMovementId == issueId && m.StateCode == "Posted"));
            var inventoryNet = await (from line in reviewer.JournalLines.AsNoTracking()
                join journal in reviewer.Journals.AsNoTracking() on line.JournalId equals journal.Id
                where line.AccountId == inventoryAccountId &&
                    (journal.State == JournalState.Posted || journal.State == JournalState.Reversed)
                select line.DebitAmount - line.CreditAmount).SumAsync();
            var payableNet = await (from line in reviewer.JournalLines.AsNoTracking()
                join journal in reviewer.Journals.AsNoTracking() on line.JournalId equals journal.Id
                where line.AccountId == payableAccountId &&
                    (journal.State == JournalState.Posted || journal.State == JournalState.Reversed)
                select line.CreditAmount - line.DebitAmount).SumAsync();
            Assert.Equal(20m, inventoryNet); // Two physical units at BDT 10 after joint contra.
            Assert.Equal(29m, payableNet); // BDT 9 credit-adjusted bill + BDT 20 replacement bill.

            // Central read models must prove the joint reversal, not merely the
            // original balanced GL entry. Every corruption probe rolls back.
            async Task AssertCentralEvidenceAsync(string expected)
            {
                var trace = await new V3EnterpriseLedgerTraceService(
                    reviewer, new Actor(tenantId, checkerId)).GetAsync(issueReference);
                Assert.True(trace.Success, trace.Message);
                Assert.Equal(expected, trace.Data!.FinanceEvidenceStatus);
                if (expected == "ExplicitReversedReference")
                {
                    Assert.Equal(stockContra.PublicId, trace.Data.ReversalReference);
                    Assert.Equal(reversalReference, trace.Data.FinanceReversalReference);
                }
                var register = await new V3EnterpriseReconciliationRegisterService(
                    reviewer, new Actor(tenantId, checkerId))
                    .GetAsync("stock", accountingDate, accountingDate, campusId, 1, 50);
                Assert.True(register.Success, register.Message);
                Assert.Contains(register.Data!.Items, x => x.Reference == issueReference &&
                    x.EvidenceStatus == expected);
            }
            await AssertCentralEvidenceAsync("ExplicitReversedReference");

            await using (var tamper = await reviewer.Database.BeginTransactionAsync())
            {
                await reviewer.JournalLines.Where(l => l.JournalId == contra.Id && l.LineNo == 1)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreditAmount, 11m));
                await reviewer.JournalLines.Where(l => l.JournalId == contra.Id && l.LineNo == 2)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.DebitAmount, 11m));
                await AssertCentralEvidenceAsync("EvidenceConflict");
                await tamper.RollbackAsync();
            }
            await AssertCentralEvidenceAsync("ExplicitReversedReference");

            await using (var tamper = await reviewer.Database.BeginTransactionAsync())
            {
                await reviewer.InventoryMovementLines.Where(l =>
                    l.InventoryMovementId == stockContra.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.UnitCost, 12m));
                await AssertCentralEvidenceAsync("EvidenceConflict");
                await tamper.RollbackAsync();
            }
            await using (var tamper = await reviewer.Database.BeginTransactionAsync())
            {
                await reviewer.Journals.Where(j => j.Id == contra.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.SourceId, 0L));
                await AssertCentralEvidenceAsync("EvidenceConflict");
                await tamper.RollbackAsync();
            }
            await using (var tamper = await reviewer.Database.BeginTransactionAsync())
            {
                // A deleted Finance contra cannot leave its posted stock reversal
                // looking reconciled, even if tenant query filters hide the tombstone.
                await reviewer.Journals.Where(j => j.Id == contra.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.IsDeleted, true));
                await AssertCentralEvidenceAsync("EvidenceConflict");
                await tamper.RollbackAsync();
            }
            await using (var tamper = await reviewer.Database.BeginTransactionAsync())
            {
                // Campus-neutral inventory is allowed, but it cannot authorize
                // a GL contra assigned to an unrelated campus.
                await reviewer.InventoryLocations.Where(l => l.Id == locationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.CampusId, (long?)null));
                await AssertCentralEvidenceAsync("ExplicitReversedReference");
                await reviewer.Journals.Where(j => j.Id == contra.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.CampusId, (long?)null));
                await AssertCentralEvidenceAsync("EvidenceConflict");
                await tamper.RollbackAsync();
            }
            await using (var tamper = await reviewer.Database.BeginTransactionAsync())
            {
                await reviewer.InventoryLocations.Where(l => l.Id == locationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.CampusId, (long?)null));
                await reviewer.Journals.Where(j => j.Id == original.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.CampusId, (long?)null));
                Assert.Equal(409, (await new V3EnterpriseLedgerTraceService(
                    reviewer, new Actor(tenantId, checkerId)).GetAsync(issueReference)).StatusCode);
                var register = await new V3EnterpriseReconciliationRegisterService(
                    reviewer, new Actor(tenantId, checkerId))
                    .GetAsync("stock", accountingDate, accountingDate, campusId, 1, 50);
                Assert.True(register.Success, register.Message);
                Assert.Contains(register.Data!.Items, x => x.Reference == issueReference &&
                    x.EvidenceStatus == "EvidenceConflict");
                await tamper.RollbackAsync();
            }
            await AssertCentralEvidenceAsync("ExplicitReversedReference");
        }
    }
}
