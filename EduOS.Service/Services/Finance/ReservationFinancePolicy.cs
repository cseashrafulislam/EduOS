using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

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

    public async Task<bool> HasPostedActivityAsync(long tenantId, long studentId,
        long studentEnrollmentId, CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || studentId <= 0 || studentEnrollmentId <= 0)
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
}
