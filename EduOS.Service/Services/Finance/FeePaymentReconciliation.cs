using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Read-only, tenant/campus-scoped receipt reconciliation. All financial tombstones are
/// inspected; no posted payment, allocation, refund or journal is ever mutated here.
/// </summary>
public sealed partial class FeeReceivablesAgingService
{
    public async Task<ApiResponse<FeePaymentReconciliationRegisterDto>> GetPaymentReconciliationAsync(
        long? campusId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed)
            return ApiResponse<FeePaymentReconciliationRegisterDto>.ErrorResponse(
                "Institutional finance access denied.", 403);
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (search?.Trim().Length ?? 0) > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<FeePaymentReconciliationRegisterDto>.ErrorResponse("Invalid reconciliation filters.");

        var tenant = _user.TenantId;
        var allowedCampuses = _db.Campuses.AsNoTracking()
            .Where(c => c.TenantId == tenant && !c.IsDeleted);
        if (!_user.IsTenantAdmin)
            allowedCampuses = allowedCampuses.Where(c => _db.UserCampusAccesses.Any(a =>
                a.TenantId == tenant && a.UserId == _user.UserId &&
                a.CampusId == c.Id && a.IsActive && !a.IsDeleted));
        if (campusId.HasValue && !await allowedCampuses.AnyAsync(x => x.Id == campusId.Value, ct))
            return ApiResponse<FeePaymentReconciliationRegisterDto>.ErrorResponse("Campus not accessible.", 403);

        // Use historical allocations for scope: a deleted allocation cannot silently make
        // a mixed-campus receipt accessible to a campus-limited accountant.
        var receiptCampuses =
            from allocation in _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            join invoice in _db.StudentInvoices.IgnoreQueryFilters().AsNoTracking()
                on new { allocation.TenantId, Id = allocation.StudentInvoiceId }
                equals new { invoice.TenantId, invoice.Id }
            join enrollment in _db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
                on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            where allocation.TenantId == tenant
            group enrollment.CampusId by allocation.StudentPaymentId into g
            select new { PaymentId = g.Key, MinCampusId = g.Min(), MaxCampusId = g.Max(),
                MatchedCount = g.Count() };
        var allocationCounts = _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant)
            .GroupBy(x => x.StudentPaymentId)
            .Select(g => new { PaymentId = g.Key, TotalCount = g.Count() });

        // Page receipts independently of optional allocation scope. A left join to a
        // grouped non-nullable campus aggregate can materialize null incorrectly under
        // EF InMemory, and should not determine tenant-admin visibility.
        var query =
            from payment in _db.StudentPayments.AsNoTracking()
            join student in _db.Students.AsNoTracking()
                on new { payment.TenantId, Id = payment.StudentId }
                equals new { student.TenantId, student.Id }
            where payment.TenantId == tenant && !payment.IsDeleted && !student.IsDeleted &&
                (payment.State == PaymentState.Successful || payment.State == PaymentState.Refunded)
            select new { payment, student };
        var safelyScopedIds =
            from scope in receiptCampuses
            join total in allocationCounts on scope.PaymentId equals total.PaymentId
            where scope.MinCampusId == scope.MaxCampusId &&
                scope.MatchedCount == total.TotalCount &&
                allowedCampuses.Any(c => c.Id == scope.MinCampusId)
            select scope.PaymentId;
        if (!_user.IsTenantAdmin)
            query = query.Where(x => safelyScopedIds.Contains(x.payment.Id));
        if (campusId.HasValue)
        {
            var selectedCampus = campusId.Value;
            var campusScopedIds =
                from scope in receiptCampuses
                join total in allocationCounts on scope.PaymentId equals total.PaymentId
                where scope.MinCampusId == selectedCampus &&
                    scope.MaxCampusId == selectedCampus &&
                    scope.MatchedCount == total.TotalCount
                select scope.PaymentId;
            query = query.Where(x => campusScopedIds.Contains(x.payment.Id));
        }
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.payment.ReceiptNumber.StartsWith(term) ||
                x.student.StudentCode.StartsWith(term) || x.student.FullName.StartsWith(term));

        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.payment.PaymentDate)
            .ThenByDescending(x => x.payment.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.payment.Id, x.payment.PublicId, x.payment.ReceiptNumber,
                x.payment.Amount, x.payment.CurrencyCode, x.payment.JournalId,
                x.payment.StudentId, x.payment.State, x.student.StudentCode,
                x.student.FullName
            }).ToListAsync(ct);
        var paymentIds = rows.Select(x => x.Id).ToArray();
        var scopes = await receiptCampuses.Where(x => paymentIds.Contains(x.PaymentId))
            .ToDictionaryAsync(x => x.PaymentId, ct);
        var totals = await allocationCounts.Where(x => paymentIds.Contains(x.PaymentId))
            .ToDictionaryAsync(x => x.PaymentId, ct);
        var allocations = await _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && paymentIds.Contains(x.StudentPaymentId))
            .ToListAsync(ct);
        if (allocations.Count > 20000)
            return ApiResponse<FeePaymentReconciliationRegisterDto>.ErrorResponse(
                "Too many allocations on this page; narrow the filters.", 409);
        var invoiceIds = allocations.Select(x => x.StudentInvoiceId).Distinct().ToArray();
        var invoices = await _db.StudentInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && invoiceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var enrollmentIds = invoices.Values.Select(x => x.StudentEnrollmentId).Distinct().ToArray();
        var enrollments = await _db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && enrollmentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var allocationIds = allocations.Select(x => x.Id).ToArray();
        var portions = await _db.RefundAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && allocationIds.Contains(x.PaymentAllocationId))
            .ToListAsync(ct);
        if (portions.Count > 20000)
            return ApiResponse<FeePaymentReconciliationRegisterDto>.ErrorResponse(
                "Too many refund allocations on this page; narrow the filters.", 409);
        var refundIds = portions.Select(x => x.RefundId).Distinct().ToArray();
        // Read all refunds belonging to the selected receipts, including paid refunds
        // with no allocation rows, plus foreign-linked refund evidence.
        var refundRows = await _db.Refunds.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant &&
                (paymentIds.Contains(x.StudentPaymentId) || refundIds.Contains(x.Id)))
            .ToListAsync(ct);
        if (refundRows.Count > 20000)
            return ApiResponse<FeePaymentReconciliationRegisterDto>.ErrorResponse(
                "Too many refunds on this page; narrow the filters.", 409);
        var refunds = refundRows.ToDictionary(x => x.Id);
        var refundEvidence = await InstitutionalRefundJournalIntegrity.LoadAsync(
            _db, tenant, refundRows, ct);
        if (refundEvidence == null)
            return ApiResponse<FeePaymentReconciliationRegisterDto>.ErrorResponse(
                "Too many refund journal lines on this page; narrow the filters.", 409);
        var journalIds = rows.Where(x => x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).Distinct().ToArray();
        var journals = await _db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && journalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var journalLines = await _db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && journalIds.Contains(x.JournalId))
            .ToListAsync(ct);
        var linesByJournal = journalLines.ToLookup(x => x.JournalId);
        var reversed = (await _db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ReversalOfJournalId.HasValue &&
                journalIds.Contains(x.ReversalOfJournalId.Value) && x.State == JournalState.Posted)
            .Select(x => x.ReversalOfJournalId!.Value).ToListAsync(ct)).ToHashSet();
        var campusIds = scopes.Values.Select(x => x.MinCampusId).Distinct().ToArray();
        var campusNames = await allowedCampuses.Where(x => campusIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var byPayment = allocations.ToLookup(x => x.StudentPaymentId);
        var byAllocation = portions.ToLookup(x => x.PaymentAllocationId);
        var resultRows = new List<FeePaymentReconciliationRowDto>(rows.Count);

        foreach (var payment in rows)
        {
            var scope = scopes.GetValueOrDefault(payment.Id);
            var campus = scope == null ? (long?)null : scope.MinCampusId;
            var maxCampus = scope == null ? (long?)null : scope.MaxCampusId;
            var resolvedCount = scope?.MatchedCount ?? 0;
            var allocationCount = totals.TryGetValue(payment.Id, out var total) ? total.TotalCount : 0;
            var issues = new List<string>();
            if (payment.CurrencyCode != "BDT" || payment.Amount <= 0m ||
                decimal.Round(payment.Amount, 2) != payment.Amount)
                issues.Add("Receipt amount or currency invalid");
            if (!campus.HasValue)
                issues.Add("Receipt has no invoice allocation or campus provenance");
            else if (campus != maxCampus)
                issues.Add("Receipt allocations span multiple campuses");
            if (resolvedCount != allocationCount)
                issues.Add("Unresolved allocation campus provenance");
            decimal gross = 0m, paidRefunds = 0m;
            foreach (var allocation in byPayment[payment.Id])
            {
                if (allocation.IsDeleted || allocation.Amount <= 0m ||
                    decimal.Round(allocation.Amount, 2) != allocation.Amount)
                {
                    issues.Add("Historical or invalid payment allocation");
                    continue;
                }
                gross += allocation.Amount;
                if (!invoices.TryGetValue(allocation.StudentInvoiceId, out var invoice) ||
                    invoice.IsDeleted || invoice.CurrencyCode != payment.CurrencyCode ||
                    !enrollments.TryGetValue(invoice.StudentEnrollmentId, out var enrollment) ||
                    enrollment.IsDeleted || enrollment.StudentId != payment.StudentId)
                    issues.Add("Invoice allocation provenance invalid");
                foreach (var portion in byAllocation[allocation.Id])
                {
                    if (portion.IsDeleted || portion.Amount <= 0m ||
                        decimal.Round(portion.Amount, 2) != portion.Amount ||
                        !refunds.TryGetValue(portion.RefundId, out var refund) ||
                        refund.IsDeleted || refund.StudentPaymentId != payment.Id ||
                        refund.State != RefundState.Paid || !refund.ReversalJournalId.HasValue)
                    {
                        issues.Add("Refund allocation provenance invalid");
                        continue;
                    }
                    paidRefunds += portion.Amount;
                    if(portion.IsCreditNote &&
                        (!campus.HasValue || campus!=maxCampus ||
                         !invoices.TryGetValue(allocation.StudentInvoiceId,out var creditedInvoice) ||
                         creditedInvoice.State!=InvoiceState.Paid ||
                         !refundEvidence.MatchesCreditNote(refund,tenant,campus.Value,
                             payment.StudentId,portion.Amount)))
                        issues.Add("Paid invoice credit-note journal requires reconciliation");
                }
            }
            if (gross != payment.Amount)
                issues.Add("Receipt amount differs from allocated invoice total");
            if (paidRefunds > gross || paidRefunds > payment.Amount)
                issues.Add("Paid refunds exceed receipt allocations");
            foreach (var refund in refunds.Values.Where(x => x.StudentPaymentId == payment.Id &&
                x.State == RefundState.Paid && !x.IsDeleted))
            {
                var allocated = portions.Where(x => x.RefundId == refund.Id && !x.IsDeleted)
                    .Sum(x => x.Amount);
                if (allocated != refund.Amount || !refund.ReversalJournalId.HasValue)
                    issues.Add("Paid refund total or reversal provenance invalid");
                if (!campus.HasValue || campus != maxCampus ||
                    !refundEvidence.Matches(refund, tenant, campus.Value, payment.StudentId))
                    issues.Add("Paid refund journal requires reconciliation");
            }
            if (payment.JournalId.HasValue &&
                (!campus.HasValue || campus != maxCampus ||
                 !journals.TryGetValue(payment.JournalId.Value, out var journal) ||
                 reversed.Contains(payment.JournalId.Value) ||
                 !InstitutionalPostingJournalIntegrity.Matches(journal,
                     linesByJournal[payment.JournalId.Value].ToArray(), tenant,
                     "StudentPayment", payment.Id, campus.Value,
                     payment.StudentId, payment.Amount,
                     linesByJournal[payment.JournalId.Value]
                         .Where(x => x.DebitAmount > 0m).Select(x => (long?)x.AccountId)
                         .FirstOrDefault())))
                issues.Add("Linked receipt journal requires reconciliation");
            resultRows.Add(new FeePaymentReconciliationRowDto
            {
                PaymentReference = payment.PublicId, ReceiptNumber = payment.ReceiptNumber,
                StudentCode = payment.StudentCode, StudentName = payment.FullName,
                CampusName = !campus.HasValue ? "Unallocated" :
                    campus != maxCampus ? "Multiple campuses" :
                    campusNames.GetValueOrDefault(campus.Value, "Campus unavailable"),
                CurrencyCode = payment.CurrencyCode, Amount = payment.Amount,
                GrossAllocated = gross, PaidRefunds = paidRefunds, NetAllocated = gross - paidRefunds,
                Status = issues.Count > 0 ? "Requires review" :
                    payment.JournalId.HasValue ? "Reconciled" : "Unposted",
                Issues = string.Join("; ", issues.Distinct())
            });
        }
        return ApiResponse<FeePaymentReconciliationRegisterDto>.SuccessResponse(
            new FeePaymentReconciliationRegisterDto
            {
                Page = page, PageSize = pageSize, TotalCount = count,
                PageExceptions = resultRows.Count(x => x.Status == "Requires review"),
                Rows = resultRows
            });
    }
}
