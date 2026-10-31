using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Conservative interim gate. Existing invoices/payments do not record a canonical
/// hostel/transport reservation reference, so any posted financial activity for the
/// enrollment/student is treated as potentially related. Do not infer a link from
/// invoice text or silently change posted obligations. Issued-but-unposted
/// invoices also block: they represent billing commitments that must be reconciled
/// before a no-charge reservation date/term mutation.
/// Tenant-filtered posting history must remain blocking even when a legacy
/// import marked a previously posted invoice or receipt as soft-deleted.
/// </summary>
public sealed class ReservationFinancePolicy : IReservationFinancePolicy
{
    private readonly EduOSDbContext _db;

    public ReservationFinancePolicy(EduOSDbContext db) => _db = db;

    public Task<bool> HasValidAutomaticChargeAsync(long tenantId, string kind, long clearanceId,
        long journalId, long campusId, long studentId, long enrollmentId, decimal amount,
        CancellationToken ct = default) => ReservationAutomaticInvoicePosting.PreparedAsync(
            _db, tenantId, kind, clearanceId, journalId, campusId, studentId, enrollmentId, amount, ct);

    public Task<bool> FinalizeAutomaticChargeAsync(long tenantId, string kind, long clearanceId,
        long journalId, long campusId, long studentId, long enrollmentId, decimal amount,
        long postedByUserId, DateTime nowUtc, CancellationToken ct = default) =>
        ReservationAutomaticInvoicePosting.FinalizeAsync(_db, tenantId, kind, clearanceId,
            journalId, campusId, studentId, enrollmentId, amount, postedByUserId, nowUtc, ct);

    public Task<bool> HasValidAutomaticCreditAsync(long tenantId, string kind, long clearanceId,
        long journalId, long campusId, long studentId, long enrollmentId, decimal signedAmount,
        CancellationToken ct = default) => ReservationAutomaticCreditNote.PreparedAsync(_db,
            tenantId, kind, clearanceId, journalId, campusId, studentId, enrollmentId,
            signedAmount, ct);

    public Task<bool> FinalizeAutomaticCreditAsync(long tenantId, string kind, long clearanceId,
        long journalId, long campusId, long studentId, long enrollmentId, decimal signedAmount,
        long postedByUserId, DateTime nowUtc, CancellationToken ct = default) =>
        ReservationAutomaticCreditNote.FinalizeAsync(_db, tenantId, kind, clearanceId,
            journalId, campusId, studentId, enrollmentId, signedAmount, postedByUserId, nowUtc, ct);

    /// <summary>
    /// Chargeable reservation terms require Finance approval even before an invoice
    /// is issued. This is a pure eligibility rule, not approval or proration.
    /// </summary>
    public static bool HasChargeableTerms(decimal monthlyCharge, decimal securityDeposit = 0m,
        bool hasFeeHead = false) =>
        monthlyCharge != 0m || securityDeposit != 0m || hasFeeHead;

    public async Task<bool> HasPostedActivityAsync(long tenantId, long studentId,
        long studentEnrollmentId, CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || studentId <= 0 || studentEnrollmentId <= 0 ||
            _db.CurrentTenantId != tenantId)
            return true;

        if (await _db.StudentInvoices.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId &&
                x.StudentEnrollmentId == studentEnrollmentId &&
                ((!x.IsDeleted && x.State == InvoiceState.Issued) || x.JournalId.HasValue || x.PaidAmount > 0m ||
                 x.State == InvoiceState.PartiallyPaid || x.State == InvoiceState.Paid ||
                 x.State == InvoiceState.Refunded), cancellationToken))
            return true;

        return await _db.StudentPayments.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.StudentId == studentId &&
            (x.JournalId.HasValue || x.State == PaymentState.Successful ||
             x.State == PaymentState.Refunded), cancellationToken);
    }

    public async Task<string?> GetExposureHashAsync(long tenantId, long studentId,
        long studentEnrollmentId, CancellationToken ct = default)
    {
        if (tenantId <= 0 || studentId <= 0 || studentEnrollmentId <= 0 || _db.CurrentTenantId != tenantId)
            return null;
        // Hash the full known source exposure (including archived historical postings).
        // A bounded failure is safer than approving based on an incomplete page.
        var invoices = await _db.StudentInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StudentEnrollmentId == studentEnrollmentId)
            .OrderBy(x => x.Id).Take(2001)
            .Select(x => new { x.Id, x.IsDeleted, x.State, x.TotalAmount,
                x.PaidAmount, x.DueAmount, x.JournalId, x.CurrencyCode })
            .ToListAsync(ct);
        var payments = await _db.StudentPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StudentId == studentId)
            .OrderBy(x => x.Id).Take(2001)
            .Select(x => new { x.Id, x.IsDeleted, x.State, x.Amount,
                x.JournalId, x.CurrencyCode }).ToListAsync(ct);
        if (invoices.Count > 2000 || payments.Count > 2000) return null;
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var paymentIds = payments.Select(x => x.Id).ToArray();
        var lines = await _db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && invoiceIds.Contains(x.StudentInvoiceId))
            .OrderBy(x => x.Id).Take(5001)
            .Select(x => new { x.Id, x.StudentInvoiceId, x.IsDeleted,
                x.FeeHeadId, x.NetAmount }).ToListAsync(ct);
        var refunds = await _db.Refunds.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && paymentIds.Contains(x.StudentPaymentId))
            .OrderBy(x => x.Id).Take(2001)
            .Select(x => new { x.Id, x.StudentPaymentId, x.IsDeleted, x.State,
                x.Amount, x.ReversalJournalId }).ToListAsync(ct);
        if (lines.Count > 5000 || refunds.Count > 2000) return null;
        var credits = await _db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SourceType == ReservationAutomaticCreditNote.SourceType &&
                x.SourceId.HasValue && invoiceIds.Contains(x.SourceId.Value))
            .OrderBy(x => x.Id).Take(2001)
            .Select(x => new { x.Id, x.SourceId, x.IsDeleted, x.State, x.IdempotencyKey,
                x.AccountingPeriodId, x.JournalDate, x.ReversalOfJournalId }).ToListAsync(ct);
        var creditIds = credits.Select(x => x.Id).ToArray();
        var creditLines = await _db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && creditIds.Contains(x.JournalId))
            .OrderBy(x => x.Id).Take(5001)
            .Select(x => new { x.Id, x.JournalId, x.IsDeleted, x.AccountId,
                x.DebitAmount, x.CreditAmount }).ToListAsync(ct);
        var allocations = await _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && invoiceIds.Contains(x.StudentInvoiceId))
            .OrderBy(x => x.Id).Take(5001)
            .Select(x => new { x.Id, x.IsDeleted, x.StudentPaymentId,
                x.StudentInvoiceId, x.Amount }).ToListAsync(ct);
        if (credits.Count > 2000 || creditLines.Count > 5000 || allocations.Count > 5000) return null;
        var json = JsonSerializer.Serialize(new { invoices, payments, lines, refunds,
            credits, creditLines, allocations });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public async Task<bool> HasValidPostedSettlementJournalAsync(long tenantId, long journalId,
        CancellationToken ct = default)
    {
        if (tenantId <= 0 || journalId <= 0 || _db.CurrentTenantId != tenantId) return false;
        var journal = await _db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Id == journalId && !x.IsDeleted &&
            x.SourceType == "Manual" && x.State == JournalState.Posted &&
            x.ApprovedByUserId.HasValue && x.PostedByUserId.HasValue &&
            x.ApprovedByUserId != x.CreatedBy, ct);
        if (journal == null || await _db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.State == JournalState.Posted &&
            x.ReversalOfJournalId == journalId, ct)) return false;
        var lines = await _db.JournalLines.AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.JournalId == journalId && !x.IsDeleted)
            .Select(x => new { x.DebitAmount, x.CreditAmount }).Take(101).ToListAsync(ct);
        return lines.Count is >= 2 and <= 100 &&
            lines.All(x => x.DebitAmount >= 0m && x.CreditAmount >= 0m &&
                (x.DebitAmount == 0m || x.CreditAmount == 0m)) &&
            lines.Sum(x => x.DebitAmount) > 0m &&
            lines.Sum(x => x.DebitAmount) == lines.Sum(x => x.CreditAmount);
    }
}
