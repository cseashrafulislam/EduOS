using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    private sealed class PostingAuditSource
    {
        public long Id { get; set; }
        public long? JournalId { get; set; }
        public DateOnly Date { get; set; }
        public long? CampusId { get; set; }
        public string Document { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
        public decimal? StockValue { get; set; }
        public decimal? UnpaidCredit { get; set; }
        public decimal? RefundReceivable { get; set; }
        public long? StockAccountId { get; set; }
        public long? PayableAccountId { get; set; }
        public long? RefundReceivableAccountId { get; set; }
        public long? FixedAssetAccountId { get; set; }
        public bool NeedsPosting { get; set; }
    }

    // One operational source per SQL-paged request. The GL remains the only
    // monetary source of truth. This audit NEVER backfills or reposts money.
    public async Task<ApiResponse<FinanceSourcePostingAuditPageDto>> GetFinanceSourcePostingAuditAsync(
        DateOnly fromDate, DateOnly toDate, string sourceKind, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (!Dates(fromDate, toDate) || page < 1 || pageSize is < 1 or > 100 ||
            string.IsNullOrWhiteSpace(sourceKind))
            return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse("Valid dates, source and page size (1–100) required.", 400);

        var tenant = user.TenantId;
        var kind = sourceKind.Trim().ToLowerInvariant();
        string expectedType;
        IQueryable<PostingAuditSource> query;
        switch (kind)
        {
            case "fee-refunds":
                return await GetFinanceRefundPostingAuditAsync(fromDate, toDate, page, pageSize, ct);
            case "fee-invoices":
                expectedType = "StudentInvoice";
                query = db.StudentInvoices.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.InvoiceDate, Document = x.InvoiceNumber, Amount = x.TotalAmount,
                        State = x.State == InvoiceState.Draft ? "Draft" :
                            x.State == InvoiceState.Cancelled ? "Cancelled" : "Issued/Paid",
                        NeedsPosting = x.State != InvoiceState.Draft && x.State != InvoiceState.Cancelled });
                break;
            case "fee-payments":
                expectedType = "StudentPayment";
                query = db.StudentPayments.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.PaymentDate, Document = x.ReceiptNumber, Amount = x.Amount,
                        State = x.State == PaymentState.Successful ? "Successful" :
                            x.State == PaymentState.Refunded ? "Refunded" : "Pending/Cancelled",
                        NeedsPosting = x.State == PaymentState.Successful || x.State == PaymentState.Refunded });
                break;
            case "admission-payments":
                expectedType = "AdmissionPayment";
                query = db.AdmissionPayments.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.PaymentDate, Document = x.ReceiptNumber, Amount = x.Amount,
                        State = x.State == PaymentState.Successful ? "Successful" :
                            x.State == PaymentState.Refunded ? "Refunded" : "Pending/Cancelled",
                        NeedsPosting = x.State == PaymentState.Successful || x.State == PaymentState.Refunded });
                break;
            case "payroll":
                expectedType = "PayrollRun";
                query = db.PayrollRuns.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.PeriodEnd, CampusId = x.CampusId, Document = x.RunNumber,
                        State = x.State == PayrollRunState.Posted ? "Posted" : "Not posted",
                        NeedsPosting = x.State == PayrollRunState.Posted });
                break;
            case "payroll-payments":
                expectedType = "PayrollPayment";
                query = db.PayrollPayments.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.PaymentDate, Document = x.PublicId.ToString(), Amount = x.Amount,
                        State = x.State == PaymentState.Successful ? "Successful" :
                            x.State == PaymentState.Refunded ? "Refunded" : "Pending/Cancelled",
                        NeedsPosting = x.State == PaymentState.Successful || x.State == PaymentState.Refunded });
                break;
            case "procurement":
                expectedType = "ProcurementSupplierInvoice";
                query = db.ProcurementSupplierInvoices.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.InvoiceDate, CampusId = x.CampusId,
                        Document = x.SupplierInvoiceNumber, Amount = x.TotalAmount,
                        StockValue = x.StockValue,
                        State = x.StateCode, NeedsPosting = x.StateCode == "Posted" || x.StateCode == "Reversed" });
                break;
            case "procurement-returns":
                expectedType = "ProcurementSupplierReturnCredit";
                query = db.ProcurementSupplierReturnCredits.AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.ReturnDate, CampusId = x.CampusId,
                        Document = x.SupplierReturnReference, Amount = x.CreditAmount,
                        StockValue = x.StockValue, UnpaidCredit = x.UnpaidCreditAmount,
                        RefundReceivable = x.RefundReceivableAmount,
                        StockAccountId = x.StockAccountId, PayableAccountId = x.PayableAccountId,
                        RefundReceivableAccountId = x.RefundReceivableAccountId,
                        State = x.StateCode, NeedsPosting = x.StateCode == "Posted" || x.StateCode == "Reversed" });
                break;
            case "facilities":
                expectedType = "FacilityVendorServiceBill";
                query = db.FacilityVendorServiceBills.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.InvoiceDate, CampusId = x.CampusId,
                        Document = x.SupplierInvoiceNumber, Amount = x.AmountBdt,
                        State = x.StateCode, NeedsPosting = x.StateCode == "Posted" || x.StateCode == "Reversed" });
                break;
            case "assets":
                expectedType = "AssetCapitalization";
                query = db.AssetCapitalizationCommands.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.CapitalizationDate, CampusId = x.CampusId,
                        Document = x.PublicId.ToString(), Amount = x.ProposedCostBasis,
                        FixedAssetAccountId = x.FixedAssetAccountId,
                        State = x.StateCode, NeedsPosting = x.StateCode == "Posted" || x.StateCode == "Reversed" });
                break;
            default:
                return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse("Unknown finance source type.", 400);
        }

        // Reverse direction matters: a posted GL entry with a deleted/missing
        // operational source must still be visible even when the source page is
        // empty. Compare against the full lifetime of the source, not merely the
        // selected source-date window (journal/source dates may legitimately differ).
        var allSourceIds = query.Select(x => x.Id);
        var orphanPostedJournalCount = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                x.SourceType == expectedType && x.JournalDate >= fromDate && x.JournalDate <= toDate &&
                (x.State == JournalState.Posted || x.State == JournalState.Reversed) &&
                (!x.SourceId.HasValue || !allSourceIds.Contains(x.SourceId.Value)), ct);
        query = query.Where(x => x.Date >= fromDate && x.Date <= toDate);
        var total = await query.CountAsync(ct);
        var pending = await query.CountAsync(x => x.JournalId == null && x.NeedsPosting, ct);
        var unposted = await query.CountAsync(x => x.JournalId == null && !x.NeedsPosting, ct);
        var offset = ((long)page - 1) * pageSize;
        var sources = offset > int.MaxValue ? new List<PostingAuditSource>() :
            await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
                .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var sourceIds = sources.Select(x => x.Id).ToArray();
        var journalIds = sources.Where(x => x.JournalId.HasValue).Select(x => x.JournalId!.Value).ToArray();
        var journals = await db.Journals.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && (journalIds.Contains(x.Id) ||
                (x.SourceType == expectedType && x.SourceId.HasValue && sourceIds.Contains(x.SourceId.Value))))
            .Select(x => new { x.Id, x.PublicId, x.JournalNumber, x.SourceType, x.SourceId,
                x.State, x.CurrencyCode, x.IsDeleted, x.CampusId })
            .Take(1001).ToListAsync(ct);
        if (journals.Count > 1000)
            return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse("Too many journals linked to the selected source page; investigate duplicates.", 422);

        var postingIds = journals.Where(x => x.State is JournalState.Posted or JournalState.Reversed)
            .Select(x => x.Id).Distinct().ToArray();
        // The source page is bounded, but a single valid payroll or fee journal
        // may have thousands of lines/accounts. Validate the complete history in
        // SQL rather than loading or silently truncating it at a fixed row limit.
        var postingLines = db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && postingIds.Contains(x.JournalId));
        var lineTotals = await postingLines.GroupBy(x => x.JournalId)
            .Select(g => new { JournalId = g.Key, Count = g.Count(),
                Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) })
            .ToDictionaryAsync(x => x.JournalId, ct);
        var repeatedLineNumbers = (await postingLines.GroupBy(x => new { x.JournalId, x.LineNo })
            .Where(g => g.Count() > 1).Select(g => g.Key.JournalId).Distinct()
            .ToArrayAsync(ct)).ToHashSet();
        var invalidLineJournals = (await (
            from line in postingLines
            join journal in db.Journals.IgnoreQueryFilters().AsNoTracking()
                on line.JournalId equals journal.Id
            where journal.TenantId == tenant && (line.IsDeleted || line.LineNo <= 0 ||
                line.DebitAmount < 0m || line.CreditAmount < 0m ||
                (line.DebitAmount > 0m) == (line.CreditAmount > 0m) ||
                (journal.CampusId.HasValue && line.CampusId.HasValue &&
                    journal.CampusId != line.CampusId) ||
                !db.Accounts.AsNoTracking().Any(a => a.Id == line.AccountId && a.TenantId == tenant &&
                    !a.IsDeleted && !a.IsGroup && a.AllowsPosting && a.CurrencyCode == "BDT"))
            select line.JournalId).Distinct().ToArrayAsync(ct)).ToHashSet();
        // A foreign-tenant line attached to a local journal must invalidate the
        // entire posting; excluding it from the balance calculation would make
        // a corrupt cross-tenant journal look legitimate in the central audit.
        var foreignLineJournalIds = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => postingIds.Contains(x.JournalId) && x.TenantId != tenant)
            .Select(x => x.JournalId).Distinct().ToArrayAsync(ct);
        var foreignLineJournals = foreignLineJournalIds.ToHashSet();
        // The total journal may balance while a source-specific GL leg is wrong.
        // Aggregate by journal/account in SQL: this accommodates supplier price
        // variance (debit != invoice total) without reading individual GL lines.
        var accountLegs = await (
            from line in postingLines
            join account in db.Accounts.AsNoTracking() on line.AccountId equals account.Id
            where account.TenantId == tenant
            group line by new { line.JournalId, line.AccountId, account.AccountType } into g
            select new { g.Key.JournalId, g.Key.AccountId, g.Key.AccountType,
                Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) })
            .Take(10001).ToListAsync(ct);
        if (accountLegs.Count > 10000)
            return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse(
                "Excessive GL account fan-out requires investigation.", 422);
        var legsByJournal = accountLegs.ToLookup(x => x.JournalId);
        var byId = journals.ToDictionary(x => x.Id);
        var bySource = journals.Where(x => x.SourceType == expectedType && x.SourceId.HasValue)
            .ToLookup(x => x.SourceId!.Value);
        var rows = new List<FinanceSourcePostingAuditRowDto>(sources.Count);
        foreach (var source in sources)
        {
            var result = new FinanceSourcePostingAuditRowDto {
                SourceId = source.Id, Date = source.Date, CampusId = source.CampusId,
                DocumentNumber = source.Document, SourceState = source.State,
                SourceAmount = source.Amount, JournalId = source.JournalId
            };
            var matches = bySource[source.Id].Where(x => !x.IsDeleted &&
                x.State is JournalState.Posted or JournalState.Reversed).ToArray();
            if (!source.JournalId.HasValue)
            {
                result.PostingStatus = matches.Length > 0 ? "BrokenLink" :
                    source.NeedsPosting ? "PendingPosting" : "NotPosted";
            }
            else if (!byId.TryGetValue(source.JournalId.Value, out var journal) || journal.IsDeleted)
            {
                result.PostingStatus = "MissingJournal";
            }
            else
            {
                result.JournalReference = journal.PublicId;
                result.JournalNumber = journal.JournalNumber;
                if (journal.SourceType != expectedType || journal.SourceId != source.Id)
                    result.PostingStatus = "SourceMismatch";
                else if (matches.Length != 1 || matches[0].Id != journal.Id)
                    result.PostingStatus = "DuplicateOrMissingSourceJournal";
                else if (journal.State is not (JournalState.Posted or JournalState.Reversed))
                    result.PostingStatus = "NotPostedJournal";
                else
                {
                    var hasTotals = lineTotals.TryGetValue(journal.Id, out var totals);
                    var debit = hasTotals ? totals!.Debit : 0m;
                    var credit = hasTotals ? totals!.Credit : 0m;
                    var invalid = journal.CurrencyCode != "BDT" || foreignLineJournals.Contains(journal.Id) ||
                        !hasTotals || totals!.Count < 2 || debit <= 0m || debit != credit ||
                        invalidLineJournals.Contains(journal.Id) || repeatedLineNumbers.Contains(journal.Id) ||
                        (source.CampusId.HasValue && journal.CampusId != source.CampusId);
                    if (!invalid && source.Amount.HasValue)
                    {
                        var legs = legsByJournal[journal.Id];
                        if (kind is "fee-invoices" or "fee-payments" or "admission-payments" or
                            "payroll-payments" or "facilities")
                            invalid = debit != source.Amount.Value;
                        else if (kind == "procurement")
                            invalid = legs.Where(x => x.AccountType == AccountType.Asset).Sum(x => x.Debit) != source.StockValue ||
                                legs.Where(x => x.AccountType == AccountType.Liability).Sum(x => x.Credit) != source.Amount.Value;
                        else if (kind == "procurement-returns")
                            invalid = source.StockAccountId is not > 0 || source.PayableAccountId is not > 0 ||
                                legs.Where(x => x.AccountId == source.StockAccountId).Sum(x => x.Credit) != source.StockValue ||
                                legs.Where(x => x.AccountId == source.PayableAccountId).Sum(x => x.Debit) != source.UnpaidCredit ||
                                (source.RefundReceivable > 0m && source.RefundReceivableAccountId is not > 0) ||
                                legs.Where(x => x.AccountId == source.RefundReceivableAccountId).Sum(x => x.Debit) != source.RefundReceivable ||
                                source.UnpaidCredit + source.RefundReceivable != source.Amount.Value;
                        else if (kind == "assets")
                            invalid = source.FixedAssetAccountId is not > 0 ||
                                legs.Where(x => x.AccountId == source.FixedAssetAccountId).Sum(x => x.Debit) != source.Amount.Value;
                    }
                    result.PostingStatus = invalid ? "InvalidJournal" :
                        !source.NeedsPosting && journal.State == JournalState.Posted ? "UnexpectedPosting" :
                        journal.State == JournalState.Reversed ? "Reversed" : "Posted";
                }
            }
            if (result.PostingStatus is not ("Posted" or "Reversed" or "NotPosted"))
                result.Finding = "Investigate the source/GL linkage before any controlled correction; never repost automatically.";
            rows.Add(result);
        }
        return ApiResponse<FinanceSourcePostingAuditPageDto>.SuccessResponse(new FinanceSourcePostingAuditPageDto {
            FromDate = fromDate, ToDate = toDate, SourceKind = kind,
            LinkedCount = total - pending - unposted,
            PendingPostingCount = pending, UnpostedCount = unposted,
            OrphanPostedJournalCount = orphanPostedJournalCount,
            Sources = new PagedResult<FinanceSourcePostingAuditRowDto> {
                Page = page, PageSize = pageSize, TotalCount = total, Items = rows
            }
        });
    }
}
