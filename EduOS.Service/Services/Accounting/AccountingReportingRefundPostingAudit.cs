using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Finance;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Refunds own their payout/credit-note subledger. Inspect its existing
    // journals without generating a second financial posting or altering a refund.
    private async Task<ApiResponse<FinanceSourcePostingAuditPageDto>> GetFinanceRefundPostingAuditAsync(
        DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken ct)
    {
        var tenant = user.TenantId;
        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue);
        var throughUtc = toDate.ToDateTime(TimeOnly.MaxValue);
        // A missing payout timestamp must remain visible for investigation.
        // Refunds have no separate business-date field; use payout UTC date,
        // falling back to request creation UTC date for incomplete/pending rows.
        var query = db.Refunds.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted &&
            (x.PaidAt ?? x.CreatedAt) >= fromUtc && (x.PaidAt ?? x.CreatedAt) <= throughUtc);
        var total = await query.CountAsync(ct);
        var pending = await query.CountAsync(x => x.State == RefundState.Paid &&
            !x.ReversalJournalId.HasValue, ct);
        var unposted = await query.CountAsync(x => x.State != RefundState.Paid &&
            !x.ReversalJournalId.HasValue, ct);
        var offset = ((long)page - 1) * pageSize;
        var refunds = offset > int.MaxValue ? [] : await query
            .OrderByDescending(x => x.PaidAt ?? x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var ids = refunds.Select(x => x.Id).ToArray();
        var paymentIds = refunds.Select(x => x.StudentPaymentId).Distinct().ToArray();
        var payments = await db.StudentPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && paymentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ReceiptNumber, x.StudentId, x.Amount,
                x.CurrencyCode, x.State, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var allocationCounts = await db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && paymentIds.Contains(x.StudentPaymentId))
            .GroupBy(x => x.StudentPaymentId)
            .Select(g => new { PaymentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PaymentId, x => x.Count, ct);
        var scopeRows = await (
            from allocation in db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            join invoice in db.StudentInvoices.IgnoreQueryFilters().AsNoTracking()
                on new { allocation.TenantId, Id = allocation.StudentInvoiceId }
                equals new { invoice.TenantId, invoice.Id }
            join enrollment in db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
                on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            where allocation.TenantId == tenant && paymentIds.Contains(allocation.StudentPaymentId)
            group enrollment by allocation.StudentPaymentId into g
            select new { PaymentId = g.Key, Count = g.Count(), MinCampus = g.Min(x => x.CampusId),
                MaxCampus = g.Max(x => x.CampusId), MinStudent = g.Min(x => x.StudentId),
                MaxStudent = g.Max(x => x.StudentId) }).ToDictionaryAsync(x => x.PaymentId, ct);
        var credited = await db.RefundAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && ids.Contains(x.RefundId) && x.IsCreditNote)
            .GroupBy(x => x.RefundId)
            .Select(g => new { RefundId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.RefundId, x => x.Amount, ct);
        var evidence = await InstitutionalRefundJournalIntegrity.LoadAsync(db, tenant, refunds, ct);
        if (evidence == null)
            return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse(
                "Too many linked refund journal rows; narrow the report.", 422);
        var journalRows = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.SourceId.HasValue && ids.Contains(x.SourceId.Value) &&
                (x.SourceType == "StudentRefund" || x.SourceType == "StudentRefundCreditNote"))
            .Select(x => new { x.Id, x.SourceId, x.SourceType, x.State,
                x.IsDeleted, x.PublicId, x.JournalNumber })
            .Take(1001).ToListAsync(ct);
        if (journalRows.Count > 1000)
            return ApiResponse<FinanceSourcePostingAuditPageDto>.ErrorResponse(
                "Too many journals linked to refund sources; investigate duplicates.", 422);
        var cashJournals = journalRows.Where(x => x.SourceType == "StudentRefund")
            .ToLookup(x => x.SourceId!.Value);
        var creditJournals = journalRows.Where(x => x.SourceType == "StudentRefundCreditNote")
            .ToLookup(x => x.SourceId!.Value);
        var rows = new List<FinanceSourcePostingAuditRowDto>(refunds.Count);
        foreach (var refund in refunds)
        {
            var paid = refund.State == RefundState.Paid;
            payments.TryGetValue(refund.StudentPaymentId, out var payment);
            scopeRows.TryGetValue(refund.StudentPaymentId, out var scope);
            allocationCounts.TryGetValue(refund.StudentPaymentId, out var allocationCount);
            var campus = scope != null && scope.MinCampus == scope.MaxCampus
                ? scope.MinCampus : (long?)null;
            var row = new FinanceSourcePostingAuditRowDto {
                SourceId = refund.Id, Date = DateOnly.FromDateTime(refund.PaidAt ?? refund.CreatedAt),
                CampusId = campus, SourceAmount = refund.Amount,
                SourceState = refund.State.ToString(),
                DocumentNumber = payment?.ReceiptNumber ?? "Refund #" + refund.Id,
                JournalId = refund.ReversalJournalId
            };
            var cash = cashJournals[refund.Id].Where(x => !x.IsDeleted &&
                x.State is JournalState.Posted or JournalState.Reversed).ToArray();
            var notes = creditJournals[refund.Id].Where(x => !x.IsDeleted &&
                x.State is JournalState.Posted or JournalState.Reversed).ToArray();
            if (!paid)
                row.PostingStatus = refund.ReversalJournalId.HasValue || cash.Length > 0 || notes.Length > 0
                    ? "UnexpectedPosting" : "NotPosted";
            else if (!refund.ReversalJournalId.HasValue)
                row.PostingStatus = cash.Length > 0 ? "BrokenLink" : "PendingPosting";
            else if (cash.Length == 0 || cash.All(x => x.Id != refund.ReversalJournalId.Value))
                row.PostingStatus = "MissingJournal";
            else
            {
                var journal = cash.Single(x => x.Id == refund.ReversalJournalId.Value);
                row.JournalReference = journal.PublicId;
                row.JournalNumber = journal.JournalNumber;
                var validScope = payment != null && !payment.IsDeleted &&
                    payment.CurrencyCode == "BDT" && payment.Amount >= refund.Amount &&
                    payment.State is PaymentState.Successful or PaymentState.Refunded &&
                    scope != null && allocationCount > 0 && scope.Count == allocationCount &&
                    scope.MinStudent == scope.MaxStudent && scope.MinStudent == payment.StudentId &&
                    scope.MinCampus == scope.MaxCampus;
                var creditAmount = credited.GetValueOrDefault(refund.Id);
                var validCredit = creditAmount > 0m
                    ? notes.Length == 1 && evidence.MatchesCreditNote(refund, tenant,
                        scope?.MinCampus ?? 0, payment?.StudentId ?? 0, creditAmount)
                    : notes.Length == 0;
                row.PostingStatus = refund.PaidAt.HasValue && validScope && cash.Length == 1 &&
                    evidence.Matches(refund, tenant, scope!.MinCampus, payment!.StudentId) && validCredit
                    ? "Posted" : "InvalidJournal";
            }
            if (row.PostingStatus is not ("Posted" or "NotPosted"))
                row.Finding = "Investigate refund, payment allocations and immutable GL evidence; never repost automatically.";
            rows.Add(row);
        }
        return ApiResponse<FinanceSourcePostingAuditPageDto>.SuccessResponse(new FinanceSourcePostingAuditPageDto {
            FromDate = fromDate, ToDate = toDate, SourceKind = "fee-refunds",
            LinkedCount = total - pending - unposted,
            PendingPostingCount = pending, UnpostedCount = unposted,
            Sources = new PagedResult<FinanceSourcePostingAuditRowDto> {
                Page = page, PageSize = pageSize, TotalCount = total, Items = rows
            }
        });
    }
}
