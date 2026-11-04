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
            case "procurement":
                expectedType = "ProcurementSupplierInvoice";
                query = db.ProcurementSupplierInvoices.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.InvoiceDate, CampusId = x.CampusId,
                        Document = x.SupplierInvoiceNumber, Amount = x.TotalAmount,
                        State = x.StateCode, NeedsPosting = x.StateCode == "Posted" });
                break;
            case "facilities":
                expectedType = "FacilityVendorServiceBill";
                query = db.FacilityVendorServiceBills.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.InvoiceDate, CampusId = x.CampusId,
                        Document = x.SupplierInvoiceNumber, Amount = x.AmountBdt,
                        State = x.StateCode, NeedsPosting = x.StateCode == "Posted" });
                break;
            case "assets":
                expectedType = "AssetCapitalization";
                query = db.AssetCapitalizationCommands.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted)
                    .Select(x => new PostingAuditSource { Id = x.Id, JournalId = x.JournalId,
                        Date = x.CapitalizationDate, CampusId = x.CampusId,
                        Document = x.PublicId.ToString(), Amount = x.ProposedCostBasis,
                        State = x.StateCode, NeedsPosting = x.StateCode == "Posted" });
                break;
            default:
                return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse("Unknown finance source type.", 400);
        }

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
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && postingIds.Contains(x.JournalId))
            .Select(x => new { x.JournalId, x.AccountId, x.LineNo, x.DebitAmount,
                x.CreditAmount, x.CampusId, x.IsDeleted }).Take(5001).ToListAsync(ct);
        if (lines.Count > 5000)
            return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse("Too many journal lines on this page; narrow the report.", 422);
        var accountIds = lines.Select(x => x.AccountId).Distinct().ToArray();
        if (accountIds.Length > 1500)
            return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse("Too many posting accounts on this page; narrow the report.", 422);
        var validAccounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && accountIds.Contains(x.Id) && !x.IsGroup && x.AllowsPosting &&
            x.CurrencyCode == "BDT").Select(x => x.Id).ToArrayAsync(ct);
        var validIds = validAccounts.ToHashSet();
        var byJournal = lines.ToLookup(x => x.JournalId);
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
                    var entries = byJournal[journal.Id].ToArray();
                    var debit = entries.Sum(x => x.DebitAmount);
                    var credit = entries.Sum(x => x.CreditAmount);
                    var invalid = journal.CurrencyCode != "BDT" || entries.Length < 2 || debit <= 0m ||
                        debit != credit || entries.Any(x => x.IsDeleted || x.LineNo <= 0 ||
                            !validIds.Contains(x.AccountId) || x.DebitAmount < 0m || x.CreditAmount < 0m ||
                            (x.DebitAmount > 0m) == (x.CreditAmount > 0m) ||
                            (journal.CampusId.HasValue && x.CampusId.HasValue &&
                             journal.CampusId != x.CampusId)) ||
                        entries.Select(x => x.LineNo).Distinct().Count() != entries.Length ||
                        (source.CampusId.HasValue && journal.CampusId != source.CampusId);
                    if (!invalid && source.Amount.HasValue &&
                        (kind is "fee-payments" or "admission-payments" or "facilities") &&
                        debit != source.Amount.Value) invalid = true;
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
            Sources = new PagedResult<FinanceSourcePostingAuditRowDto> {
                Page = page, PageSize = pageSize, TotalCount = total, Items = rows
            }
        });
    }
}
