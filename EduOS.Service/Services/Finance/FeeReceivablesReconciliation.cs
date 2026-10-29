using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Read-only institutional invoice-to-receipt reconciliation. Bounded page materialization,
/// with historical tombstones included so deleted financial evidence cannot disappear.
/// </summary>
public sealed partial class FeeReceivablesAgingService
{
    public async Task<ApiResponse<FeeReconciliationRegisterDto>> GetReconciliationAsync(
        long? campusId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed)
            return ApiResponse<FeeReconciliationRegisterDto>.ErrorResponse("Institutional finance access denied.", 403);
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (search?.Trim().Length ?? 0) > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<FeeReconciliationRegisterDto>.ErrorResponse("Invalid reconciliation filters.");

        var tenant = _user.TenantId;
        var allowedCampuses = _db.Campuses.AsNoTracking()
            .Where(c => c.TenantId == tenant && !c.IsDeleted);
        if (!_user.IsTenantAdmin)
            allowedCampuses = allowedCampuses.Where(c => _db.UserCampusAccesses.Any(a =>
                a.TenantId == tenant && a.UserId == _user.UserId &&
                a.CampusId == c.Id && a.IsActive && !a.IsDeleted));
        if (campusId.HasValue && !await allowedCampuses.AnyAsync(x => x.Id == campusId.Value, ct))
            return ApiResponse<FeeReconciliationRegisterDto>.ErrorResponse("Campus not accessible.", 403);

        var query = from invoice in _db.StudentInvoices.AsNoTracking()
            join enrollment in _db.StudentEnrollments.AsNoTracking()
                on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            join campus in allowedCampuses
                on new { enrollment.TenantId, Id = enrollment.CampusId }
                equals new { campus.TenantId, campus.Id }
            where invoice.TenantId == tenant && !invoice.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted
            select new { invoice, enrollment, student, campus };
        if (campusId.HasValue) query = query.Where(x => x.campus.Id == campusId.Value);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.invoice.InvoiceNumber.StartsWith(term) ||
                x.student.StudentCode.StartsWith(term) || x.student.FullName.StartsWith(term));

        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.invoice.InvoiceDate)
            .ThenByDescending(x => x.invoice.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new {
                x.invoice.Id, x.invoice.PublicId, x.invoice.InvoiceNumber,
                x.invoice.TotalAmount, x.invoice.PaidAmount, x.invoice.DueAmount,
                x.invoice.CurrencyCode, x.invoice.JournalId, x.invoice.State,
                x.student.StudentCode, x.student.FullName,
                StudentId = x.student.Id, CampusId = x.campus.Id, CampusName = x.campus.Name
            }).ToListAsync(ct);
        var invoiceIds = rows.Select(x => x.Id).ToArray();
        var allocations = await _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && invoiceIds.Contains(x.StudentInvoiceId))
            .ToListAsync(ct);
        if (allocations.Count > 20000)
            return ApiResponse<FeeReconciliationRegisterDto>.ErrorResponse(
                "Too many allocations on this page; narrow the filters.", 409);
        var paymentIds = allocations.Select(x => x.StudentPaymentId).Distinct().ToArray();
        var payments = await _db.StudentPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && paymentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var allocationIds = allocations.Select(x => x.Id).ToArray();
        var refundAllocations = await _db.RefundAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && allocationIds.Contains(x.PaymentAllocationId))
            .ToListAsync(ct);
        if (refundAllocations.Count > 20000)
            return ApiResponse<FeeReconciliationRegisterDto>.ErrorResponse(
                "Too many refund allocations on this page; narrow the filters.", 409);
        var refundIds = refundAllocations.Select(x => x.RefundId).Distinct().ToArray();
        var refunds = await _db.Refunds.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && refundIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var refundEvidence = await InstitutionalRefundJournalIntegrity.LoadAsync(
            _db, tenant, refunds.Values.ToArray(), ct);
        if (refundEvidence == null)
            return ApiResponse<FeeReconciliationRegisterDto>.ErrorResponse(
                "Too many refund journal lines on this page; narrow the filters.", 409);
        var journalIds = rows.Where(x => x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).Distinct().ToArray();
        var journals = await _db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && journalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var lines = await _db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && journalIds.Contains(x.JournalId))
            .ToListAsync(ct);
        var byJournal = lines.ToLookup(x => x.JournalId);
        var reversed = (await _db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ReversalOfJournalId.HasValue &&
                journalIds.Contains(x.ReversalOfJournalId.Value) && x.State == JournalState.Posted)
            .Select(x => x.ReversalOfJournalId!.Value).ToListAsync(ct)).ToHashSet();
        var byInvoice = allocations.ToLookup(x => x.StudentInvoiceId);
        var byAllocation = refundAllocations.ToLookup(x => x.PaymentAllocationId);
        var results = new List<FeeReconciliationRowDto>(rows.Count);
        foreach (var invoice in rows)
        {
            var issues = new List<string>();
            if (invoice.CurrencyCode != "BDT" || invoice.TotalAmount < 0m ||
                invoice.PaidAmount < 0m || invoice.DueAmount < 0m ||
                invoice.PaidAmount + invoice.DueAmount != invoice.TotalAmount ||
                decimal.Round(invoice.TotalAmount, 2) != invoice.TotalAmount ||
                decimal.Round(invoice.PaidAmount, 2) != invoice.PaidAmount)
                issues.Add("Invoice monetary totals or currency invalid");
            decimal gross = 0m, refunded = 0m, credited = 0m;
            foreach (var allocation in byInvoice[invoice.Id])
            {
                if (allocation.IsDeleted || allocation.Amount <= 0m ||
                    decimal.Round(allocation.Amount, 2) != allocation.Amount)
                {
                    issues.Add("Historical or invalid payment allocation");
                    continue;
                }
                if (!payments.TryGetValue(allocation.StudentPaymentId, out var payment) ||
                    payment.IsDeleted || payment.State != PaymentState.Successful ||
                    payment.StudentId != invoice.StudentId ||
                    payment.CurrencyCode != invoice.CurrencyCode)
                {
                    issues.Add("Payment source missing or inconsistent");
                    continue;
                }
                gross += allocation.Amount;
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
                    refunded += portion.Amount;
                    if (!refundEvidence.Matches(refund, tenant, invoice.CampusId, invoice.StudentId))
                        issues.Add("Paid refund journal requires reconciliation");
                    if(portion.IsCreditNote)
                    {
                        credited+=portion.Amount;
                        if(!refundEvidence.MatchesCreditNote(refund,tenant,invoice.CampusId,
                            invoice.StudentId,portion.Amount))
                            issues.Add("Paid-invoice credit note journal requires reconciliation");
                    }
                }
            }
            var net = gross - refunded + credited;
            if (net < 0m || net != invoice.PaidAmount)
                issues.Add("Paid balance differs from receipts less cash refunds plus immutable credit notes");
            if (invoice.JournalId.HasValue &&
                (!journals.TryGetValue(invoice.JournalId.Value, out var journal) ||
                 reversed.Contains(invoice.JournalId.Value) ||
                 !InstitutionalPostingJournalIntegrity.Matches(journal,
                     byJournal[invoice.JournalId.Value].ToArray(), tenant,
                     "StudentInvoice", invoice.Id, invoice.CampusId,
                     invoice.StudentId, invoice.TotalAmount)))
                issues.Add("Linked invoice journal requires reconciliation");
            results.Add(new FeeReconciliationRowDto
            {
                InvoiceReference = invoice.PublicId, InvoiceNumber = invoice.InvoiceNumber,
                StudentCode = invoice.StudentCode, StudentName = invoice.FullName,
                CampusName = invoice.CampusName, CurrencyCode = invoice.CurrencyCode,
                TotalAmount = invoice.TotalAmount, PaidAmount = invoice.PaidAmount,
                DueAmount = invoice.DueAmount, NetAllocatedAmount = net,
                CreditNoteAmount = credited,
                Status = issues.Count > 0 ? "Requires review" :
                    invoice.JournalId.HasValue ? "Reconciled" : "Unposted",
                Issues = string.Join("; ", issues.Distinct())
            });
        }
        return ApiResponse<FeeReconciliationRegisterDto>.SuccessResponse(
            new FeeReconciliationRegisterDto
            {
                Page = page, PageSize = pageSize, TotalCount = count,
                Rows = results, PageExceptions = results.Count(x => x.Status == "Requires review")
            });
    }
}
