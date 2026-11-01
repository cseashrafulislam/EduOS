using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

/// <summary>
/// Exact persisted PO -> accepted receipt -> stock movement -> supplier bill ->
/// V1 Finance payable/contra evidence. No posting, guessed association or shadow GL.
/// </summary>
public sealed class V3EnterpriseSupplierPayableTraceService(
    EduOSDbContext db, ICurrentUserService user)
{
    private ApiResponse<V3EnterpriseSupplierPayableTraceDto> Conflict(string message) =>
        ApiResponse<V3EnterpriseSupplierPayableTraceDto>.ErrorResponse(message, 409);

    public async Task<ApiResponse<V3EnterpriseSupplierPayableTraceDto>> GetAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return ApiResponse<V3EnterpriseSupplierPayableTraceDto>.ErrorResponse("Access denied.", 403);
        if (reference == Guid.Empty)
            return ApiResponse<V3EnterpriseSupplierPayableTraceDto>.ErrorResponse("Invoice reference required.", 400);
        var invoice = await db.ProcurementSupplierInvoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted, ct);
        if (invoice == null)
            return ApiResponse<V3EnterpriseSupplierPayableTraceDto>.ErrorResponse("Invoice not found.", 404);

        // Historical SQL corruption must not certify a balanced payable as
        // independently approved. Only the canonical maker/checker workflow posts.
        if ((invoice.StateCode is "Posted" or "Reversed") &&
            (invoice.CreatedBy is null or <= 0 ||
             invoice.SubmittedByUserId != invoice.CreatedBy ||
             invoice.ReviewedByUserId is null or <= 0 ||
             invoice.ReviewedByUserId == invoice.CreatedBy ||
             invoice.ReviewedByUserId == invoice.SubmittedByUserId ||
             !invoice.SubmittedAtUtc.HasValue || !invoice.ReviewedAtUtc.HasValue ||
             invoice.ReviewedAtUtc.Value < invoice.SubmittedAtUtc.Value ||
             invoice.ReviewRequestId is null || invoice.ReviewRequestId == Guid.Empty ||
             string.IsNullOrWhiteSpace(invoice.ReviewFingerprint) ||
             string.IsNullOrWhiteSpace(invoice.ReviewReason)))
            return Conflict("Supplier invoice maker-checker approval evidence conflicts.");

        var order = await db.ProcurementPurchaseOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.Id == invoice.PurchaseOrderId)
            .Select(x => new { x.Id, x.PublicId, x.CampusId, x.SupplierId, x.IsDeleted })
            .SingleOrDefaultAsync(ct);
        var supplier = await db.ProcurementSuppliers.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.Id == invoice.SupplierId)
            .Select(x => new { x.PublicId, x.CampusId, x.IsDeleted })
            .SingleOrDefaultAsync(ct);
        if (order == null || order.IsDeleted || supplier == null || supplier.IsDeleted ||
            order.CampusId != invoice.CampusId || supplier.CampusId != invoice.CampusId ||
            order.SupplierId != invoice.SupplierId ||
            !await db.Campuses.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == invoice.CampusId && !x.IsDeleted, ct))
            return Conflict("Supplier invoice PO, supplier or campus evidence conflicts.");

        var lines = await db.ProcurementSupplierInvoiceLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.SupplierInvoiceId == invoice.Id)
            .OrderBy(x => x.LineNo).Take(101).ToListAsync(ct);
        if (lines.Count is < 1 or > 100 || lines.Any(x => x.IsDeleted || x.Quantity <= 0m ||
            x.GoodsReceiptLineId <= 0 || x.PurchaseOrderLineId <= 0 ||
            x.InvoiceAmount < 0m || x.StockValue < 0m) ||
            lines.Select((x, i) => x.LineNo == i + 1).Any(x => !x) ||
            lines.Select(x => x.GoodsReceiptLineId).Distinct().Count() != lines.Count ||
            lines.Sum(x => x.InvoiceAmount) != invoice.TotalAmount ||
            lines.Sum(x => x.StockValue) != invoice.StockValue ||
            invoice.TotalAmount <= 0m || invoice.StockValue <= 0m ||
            invoice.PriceVariance != invoice.TotalAmount - invoice.StockValue)
            return Conflict("Supplier invoice line valuation or immutable receipt references conflict.");

        var receiptLineIds = lines.Select(x => x.GoodsReceiptLineId).ToArray();
        var delivered = await db.ProcurementGoodsReceiptLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && receiptLineIds.Contains(x.Id))
            .Select(x => new { x.Id, x.GoodsReceiptId, x.PurchaseOrderLineId,
                x.InventoryItemId, x.LotOrBatchNo, x.Quantity, x.PostedUnitCost,
                x.ApprovedPoUnitPrice, x.IsDeleted }).ToListAsync(ct);
        var deliveredMap = delivered.ToDictionary(x => x.Id);
        if (delivered.Count != lines.Count || lines.Any(x =>
            !deliveredMap.TryGetValue(x.GoodsReceiptLineId, out var d) ||
            d.IsDeleted || d.PurchaseOrderLineId != x.PurchaseOrderLineId ||
            d.InventoryItemId != x.InventoryItemId ||
            d.LotOrBatchNo != x.LotOrBatchNo ||
            x.Quantity > d.Quantity || x.ReceivedQuantitySnapshot != d.Quantity ||
            x.PoUnitPriceSnapshot != d.ApprovedPoUnitPrice ||
            !d.PostedUnitCost.HasValue ||
            x.StockUnitCostSnapshot != d.PostedUnitCost.Value))
            return Conflict("Supplier invoice line does not match its exact posted receipt line.");

        var receiptIds = delivered.Select(x => x.GoodsReceiptId).Distinct().ToArray();
        var receipts = await db.ProcurementGoodsReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && receiptIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.PurchaseOrderId, x.CampusId,
                x.PostedMovementId, x.IsDeleted, x.StateCode }).ToListAsync(ct);
        if (receipts.Count != receiptIds.Length || receipts.Any(x => x.IsDeleted ||
            x.PurchaseOrderId != order.Id || x.CampusId != invoice.CampusId ||
            x.StateCode != "Posted" || !x.PostedMovementId.HasValue))
            return Conflict("Receipt belongs to another PO/campus or was not posted.");
        var movementIds = receipts.Select(x => x.PostedMovementId!.Value).ToArray();
        if (movementIds.Distinct().Count() != movementIds.Length)
            return Conflict("Multiple receipts claim one posted stock movement.");
        var movements = await db.InventoryMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && movementIds.Contains(x.Id))
            .Select(x => new V3EnterpriseProcurementReceiptEvidenceValidator.Movement(
                x.Id, x.SourceId, x.FromLocationId, x.ToLocationId,
                x.StateCode, x.MovementTypeCode)).ToListAsync(ct);
        if (movements.Count != movementIds.Length)
            return Conflict("Missing canonical posted stock movement.");
        // The existing Inventory-owned validator checks every stock/receipt/PO line
        // and its exact lot, quantity and unit-cost multiset. Batch to its 50 cap.
        for (var offset = 0; offset < movements.Count; offset += 50)
        {
            var batch = movements.Skip(offset).Take(50).ToArray();
            var verified = await V3EnterpriseProcurementReceiptEvidenceValidator.InspectAsync(
                db, user.TenantId, batch, ct);
            if (verified.Invalid.Count != 0 || verified.Verified.Count != batch.Length ||
                verified.Verified.Values.Any(x => x.PurchaseOrderReference != order.PublicId))
                return Conflict("Posted receipt-to-stock provenance requires reconciliation.");
        }

        var journalIds = new[] { invoice.JournalId, invoice.ReversalJournalId }
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        if (invoice.ReversalJournalId.HasValue && !invoice.JournalId.HasValue)
            return Conflict("Reversal has no original payable.");
        if (journalIds.Length > 0 && await db.ProcurementSupplierInvoices.IgnoreQueryFilters()
            .AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId && x.Id != invoice.Id &&
                ((x.JournalId.HasValue && journalIds.Contains(x.JournalId.Value)) ||
                 (x.ReversalJournalId.HasValue &&
                    journalIds.Contains(x.ReversalJournalId.Value))), ct))
            return Conflict("Payable journal referenced by multiple supplier invoices.");
        var linked = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId &&
                ((x.SourceId == invoice.Id &&
                  (x.SourceType == "ProcurementSupplierInvoice" ||
                   x.SourceType == "ProcurementSupplierInvoiceReversal")) ||
                 journalIds.Contains(x.Id)))
            .OrderBy(x => x.Id).Take(101).ToListAsync(ct);
        if (linked.Count > 100 || linked.Any(x => x.IsDeleted || x.CampusId != invoice.CampusId ||
            x.CurrencyCode != "BDT"))
            return Conflict("Deleted, excessive or cross-campus payable journal evidence.");
        var original = linked.Where(x => x.SourceType == "ProcurementSupplierInvoice" &&
            x.SourceId == invoice.Id).ToArray();
        if (original.Length > 1 || (invoice.JournalId.HasValue &&
            (original.Length != 1 || original[0].Id != invoice.JournalId.Value)) ||
            (!invoice.JournalId.HasValue && original.Length != 0))
            return Conflict("Payable source ID is missing, duplicated or inconsistent.");
        var contra = invoice.ReversalJournalId.HasValue
            ? linked.SingleOrDefault(x => x.Id == invoice.ReversalJournalId.Value)
            : null;
        if (invoice.ReversalJournalId.HasValue && (contra == null ||
            contra.SourceType != "ProcurementSupplierInvoiceReversal" ||
            contra.SourceId != invoice.Id ||
            contra.ReversalOfJournalId != invoice.JournalId))
            return Conflict("Reversal source ID is inconsistent.");
        if (linked.Any(x => x.SourceType == "ProcurementSupplierInvoiceReversal" &&
            x.SourceId == invoice.Id && x.Id != invoice.ReversalJournalId &&
            x.State == JournalState.Posted))
            return Conflict("Unlinked posted reversal detected.");

        var allLines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && journalIds.Contains(x.JournalId))
            .OrderBy(x => x.JournalId).ThenBy(x => x.LineNo).Take(201).ToListAsync(ct);
        if (allLines.Count > 200 || allLines.Any(x => x.IsDeleted ||
            x.CampusId != invoice.CampusId || x.DebitAmount < 0m || x.CreditAmount < 0m ||
            (x.DebitAmount > 0m) == (x.CreditAmount > 0m)))
            return Conflict("Deleted, cross-campus or invalid Finance journal lines.");
        var byJournal = allLines.GroupBy(x => x.JournalId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var j in linked.Where(x => journalIds.Contains(x.Id)))
        {
            if (!byJournal.TryGetValue(j.Id, out var group) ||
                group.Length is < 2 or > 100 ||
                group.Select((x, i) => x.LineNo == i + 1).Any(x => !x) ||
                group.Sum(x => x.DebitAmount) <= 0m ||
                group.Sum(x => x.DebitAmount) != group.Sum(x => x.CreditAmount))
                return Conflict("Finance journal lines are incomplete or unbalanced.");
        }
        if (invoice.JournalId.HasValue)
        {
            var j = original[0];
            var sourceLines = byJournal[j.Id];
            if (j.State != JournalState.Posted || j.JournalDate != invoice.InvoiceDate ||
                j.ReversalOfJournalId.HasValue || sourceLines.Length !=
                    (invoice.PriceVariance == 0m ? 2 : 3) ||
                j.ApprovedByUserId != invoice.ReviewedByUserId ||
                !await db.AccountingPeriods.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == j.AccountingPeriodId &&
                    !x.IsDeleted && x.StartDate <= j.JournalDate && x.EndDate >= j.JournalDate, ct))
                return Conflict("Original Finance posting metadata is inconsistent.");
            var accountIds = sourceLines.Select(x => x.AccountId).ToArray();
            if (accountIds.Distinct().Count() != accountIds.Length)
                return Conflict("Finance posting reused an account across distinct legs.");
            var accounts = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && accountIds.Contains(x.Id))
                .Select(x => new { x.Id, x.AccountType, x.IsDeleted })
                .ToDictionaryAsync(x => x.Id, ct);
            if (accounts.Count != accountIds.Length || accounts.Values.Any(x => x.IsDeleted) ||
                !sourceLines.Any(x => accounts[x.AccountId].AccountType == AccountType.Asset &&
                    x.DebitAmount == invoice.StockValue && x.CreditAmount == 0m) ||
                !sourceLines.Any(x => accounts[x.AccountId].AccountType == AccountType.Liability &&
                    x.CreditAmount == invoice.TotalAmount && x.DebitAmount == 0m) ||
                (invoice.PriceVariance != 0m && !sourceLines.Any(x =>
                    accounts[x.AccountId].AccountType == AccountType.Expense &&
                    (invoice.PriceVariance > 0m
                        ? x.DebitAmount == invoice.PriceVariance && x.CreditAmount == 0m
                        : x.CreditAmount == -invoice.PriceVariance && x.DebitAmount == 0m))))
                return Conflict("Payable, stock valuation and variance Finance legs disagree.");
            if (contra != null)
            {
                var reversedLines = byJournal[contra.Id];
                if (contra.JournalDate < j.JournalDate ||
                    !await db.AccountingPeriods.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.Id == contra.AccountingPeriodId &&
                        !x.IsDeleted && x.StartDate <= contra.JournalDate &&
                        x.EndDate >= contra.JournalDate, ct) ||
                    reversedLines.Length != sourceLines.Length ||
                    sourceLines.Where((x, i) => reversedLines[i].AccountId != x.AccountId ||
                        reversedLines[i].LineNo != x.LineNo ||
                        reversedLines[i].DebitAmount != x.CreditAmount ||
                        reversedLines[i].CreditAmount != x.DebitAmount).Any())
                    return Conflict("Contra does not mirror the exact original payable lines.");
            }
        }
        if ((invoice.StateCode is "Posted" or "Reversed") != invoice.JournalId.HasValue ||
            (invoice.StateCode == "Reversed" && (contra == null ||
                contra.State != JournalState.Posted)) ||
            (invoice.StateCode == "Posted" && contra?.State == JournalState.Posted))
            return Conflict("Supplier invoice lifecycle and Finance journal disagree.");

        // Canonical Finance settlement instructions are never evidence of payment until
        // independently approved AND posted with external bank confirmation. Posted
        // contra journals may exactly reverse a confirmed settlement, not its history.
        var instructions = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId &&
                x.SourceType == "ProcurementSupplierPayment" && x.SourceId == invoice.Id)
            .OrderBy(x => x.Id).Take(101).ToListAsync(ct);
        if (instructions.Count > 100 || (instructions.Count > 0 && !invoice.JournalId.HasValue))
            return Conflict("Supplier settlement instruction source or fan-out conflicts.");
        var settlementEvidence = new List<V3EnterpriseSettlementInstructionEvidenceDto>();
        decimal reservedSettlement = 0m;
        decimal postedSettlement = 0m;
        if (instructions.Count > 0)
        {
            var payableAccounts = byJournal[original[0].Id]
                .Where(x => x.CreditAmount == invoice.TotalAmount && x.DebitAmount == 0m)
                .Select(x => x.AccountId).ToArray();
            if (payableAccounts.Length != 1)
                return Conflict("Canonical supplier payable leg is ambiguous.");
            var ids = instructions.Select(x => x.Id).ToArray();
            var legs = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && ids.Contains(x.JournalId))
                .OrderBy(x => x.JournalId).ThenBy(x => x.LineNo).Take(201).ToListAsync(ct);
            if (legs.Count > 200 || legs.Any(x => x.IsDeleted || x.CampusId != invoice.CampusId))
                return Conflict("Deleted, excessive or cross-campus settlement lines.");
            var groups = legs.GroupBy(x => x.JournalId).ToDictionary(x => x.Key, x => x.ToArray());
            var bankAccounts = legs.Where(x => x.LineNo == 2).Select(x => x.AccountId).Distinct().ToArray();
            var banks = await (from b in db.BankAccounts.IgnoreQueryFilters().AsNoTracking()
                join a in db.Accounts.IgnoreQueryFilters().AsNoTracking() on b.AccountId equals a.Id
                where b.TenantId == user.TenantId && a.TenantId == user.TenantId &&
                    bankAccounts.Contains(b.AccountId) && !b.IsDeleted && !a.IsDeleted &&
                    b.CurrencyCode == "BDT" && a.CurrencyCode == "BDT" &&
                    a.AccountType == AccountType.Asset &&
                    (b.CampusId == null || b.CampusId == invoice.CampusId)
                select new { b.AccountId, b.Id }).ToListAsync(ct);
            var periodIds = instructions.Select(x => x.AccountingPeriodId).Distinct().ToArray();
            var reversals = await db.Journals.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.SourceType == "ProcurementSupplierPaymentReversal" &&
                x.SourceId.HasValue && ids.Contains(x.SourceId.Value))
                .OrderBy(x => x.Id).Take(101).ToListAsync(ct);
            if (reversals.Count > 100 || reversals.Any(x => x.IsDeleted ||
                x.ReversalOfJournalId != x.SourceId || x.CampusId != invoice.CampusId ||
                x.CurrencyCode != "BDT"))
                return Conflict("Settlement reversal provenance conflicts.");
            var reversalIds = reversals.Select(x => x.Id).ToArray();
            var reversalLegs = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && reversalIds.Contains(x.JournalId))
                .OrderBy(x => x.JournalId).ThenBy(x => x.LineNo).Take(201).ToListAsync(ct);
            if (reversalLegs.Count > 200 || reversalLegs.Any(x => x.IsDeleted))
                return Conflict("Settlement reversal lines deleted or unbounded.");
            var reversalsByJournal = reversals.GroupBy(x => x.SourceId!.Value)
                .ToDictionary(x => x.Key, x => x.ToArray());
            var reversalLinesByJournal = reversalLegs.GroupBy(x => x.JournalId)
                .ToDictionary(x => x.Key, x => x.ToArray());
            periodIds = periodIds.Concat(reversals.Select(x => x.AccountingPeriodId)).Distinct().ToArray();
            var periods = await db.AccountingPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && periodIds.Contains(x.Id) && !x.IsDeleted)
                .Select(x => new { x.Id, x.StartDate, x.EndDate }).ToListAsync(ct);
            foreach (var j in instructions)
            {
                if (j.IsDeleted || j.CampusId != invoice.CampusId || j.CurrencyCode != "BDT" ||
                    j.JournalDate < invoice.InvoiceDate || j.ReversalOfJournalId.HasValue ||
                    j.CreatedBy is null or <= 0 ||
                    !periods.Any(p => p.Id == j.AccountingPeriodId &&
                        p.StartDate <= j.JournalDate && p.EndDate >= j.JournalDate) ||
                    !groups.TryGetValue(j.Id, out var pair) || pair.Length != 2 ||
                    pair[0].LineNo != 1 || pair[1].LineNo != 2 ||
                    pair[0].AccountId != payableAccounts[0] || pair[0].DebitAmount <= 0m ||
                    pair[0].CreditAmount != 0m || pair[1].DebitAmount != 0m ||
                    pair[1].CreditAmount != pair[0].DebitAmount ||
                    banks.Count(b => b.AccountId == pair[1].AccountId) != 1)
                    return Conflict("Settlement bank, campus or balanced instruction evidence conflicts.");
                if (j.State == JournalState.Approved)
                {
                    if (j.ApprovedByUserId is null or <= 0 ||
                        j.ApprovedByUserId == j.CreatedBy ||
                        j.ApprovedByUserId == invoice.CreatedBy || !j.ApprovedAt.HasValue)
                        return Conflict("Settlement independent reviewer evidence conflicts.");
                }
                else if (j.State == JournalState.Posted)
                {
                    if (j.ReferenceNumber is null || j.ReferenceNumber.Length is < 6 or > 100 ||
                        j.ApprovedByUserId is null or <= 0 || j.ApprovedByUserId == j.CreatedBy ||
                        j.ApprovedByUserId == invoice.CreatedBy || !j.ApprovedAt.HasValue ||
                        j.PostedByUserId is null or <= 0 || !j.PostedAt.HasValue ||
                        j.PostedAt < j.ApprovedAt ||
                        !(j.Description ?? "").Contains(" | Bank-confirmed (", StringComparison.Ordinal))
                        return Conflict("Posted settlement lacks independent approval or bank evidence.");
                }
                else if (j.State != JournalState.Submitted && j.State != JournalState.Rejected)
                    return Conflict("Unsupported supplier settlement journal lifecycle.");
                if (j.State != JournalState.Posted &&
                    (j.ReferenceNumber != invoice.SupplierInvoiceNumber || j.PostedAt.HasValue ||
                     j.PostedByUserId.HasValue))
                    return Conflict("An unposted settlement claims bank execution.");
                if (j.State is JournalState.Submitted or JournalState.Approved)
                    reservedSettlement += pair[0].DebitAmount;
                Guid? postedReversal = null;
                if (reversalsByJournal.TryGetValue(j.Id, out var contraRows))
                {
                    if (j.State != JournalState.Posted || contraRows.Count(x => x.State == JournalState.Posted) > 1)
                        return Conflict("Settlement reversal state or cardinality conflicts.");
                    foreach (var r in contraRows)
                    {
                        if (r.JournalDate < j.JournalDate ||
                            !periods.Any(p => p.Id == r.AccountingPeriodId &&
                                p.StartDate <= r.JournalDate && p.EndDate >= r.JournalDate) ||
                            !reversalLinesByJournal.TryGetValue(r.Id, out var mirror) || mirror.Length != 2 ||
                            mirror.Where((x,i) => x.CampusId != pair[i].CampusId ||
                                x.AccountId != pair[i].AccountId || x.LineNo != pair[i].LineNo ||
                                x.DebitAmount != pair[i].CreditAmount ||
                                x.CreditAmount != pair[i].DebitAmount).Any())
                            return Conflict("Settlement contra fails exact posted-line mirror.");
                        if (r.State == JournalState.Posted)
                        {
                            if (r.CreatedBy is null or <= 0 || r.ApprovedByUserId is null or <= 0 ||
                                r.CreatedBy == r.ApprovedByUserId || r.PostedByUserId != r.ApprovedByUserId ||
                                !r.ApprovedAt.HasValue || !r.PostedAt.HasValue)
                                return Conflict("Posted settlement contra lacks independent approval.");
                            postedReversal = r.PublicId;
                        }
                        else if (r.State != JournalState.Submitted && r.State != JournalState.Rejected)
                            return Conflict("Unsupported settlement reversal lifecycle.");
                    }
                }
                if (j.State == JournalState.Posted && !postedReversal.HasValue)
                    postedSettlement += pair[0].DebitAmount;
                settlementEvidence.Add(new V3EnterpriseSettlementInstructionEvidenceDto
                {
                    Reference = j.PublicId, State = j.State.ToString(),
                    PlannedDate = j.JournalDate, Amount = pair[0].DebitAmount,
                    IsPostedToFinance = j.State == JournalState.Posted,
                    PostedReversalReference = postedReversal
                });
            }
            if (reservedSettlement + postedSettlement > invoice.TotalAmount)
                return Conflict("Supplier settlement reservations exceed posted payable.");
        }
        var evidence = linked.Where(x => journalIds.Contains(x.Id)).Select(j =>
        {
            var group = byJournal[j.Id];
            return new V3EnterpriseJournalEvidenceDto {
                Reference = j.PublicId, Number = j.JournalNumber,
                State = j.State.ToString(), CampusId = j.CampusId,
                Debit = group.Sum(x => x.DebitAmount),
                Credit = group.Sum(x => x.CreditAmount), Balanced = true
            };
        }).ToArray();
        return ApiResponse<V3EnterpriseSupplierPayableTraceDto>.SuccessResponse(
            new V3EnterpriseSupplierPayableTraceDto {
                InvoiceReference = invoice.PublicId, InvoiceNumber = invoice.SupplierInvoiceNumber,
                PurchaseOrderReference = order.PublicId, SupplierReference = supplier.PublicId,
                CampusId = invoice.CampusId, State = invoice.StateCode,
                TotalAmount = invoice.TotalAmount, StockValue = invoice.StockValue,
                PriceVariance = invoice.PriceVariance, LineCount = lines.Count,
                GoodsReceiptReferences = receipts.Select(x => x.PublicId).OrderBy(x => x).ToArray(),
                FinanceEvidenceStatus = invoice.StateCode == "Reversed" ? "ExplicitReversedReference" :
                    contra != null ? "ReversalPending" :
                    invoice.JournalId.HasValue ? "ExplicitBalancedReference" : "NoExplicitReference",
                Journals = evidence,
                SettlementReservedAmount = reservedSettlement,
                SettlementPostedNetAmount = postedSettlement,
                SourceLinkedOutstandingAmount = invoice.TotalAmount - postedSettlement,
                SettlementInstructions = settlementEvidence
            });
    }
}
