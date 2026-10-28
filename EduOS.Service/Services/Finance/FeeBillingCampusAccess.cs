using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Never infer campus access from a tenant role or student reference. Grants are
    // resolved on the server for the authenticated member, within the active tenant.
    private IQueryable<long> GrantedBillingCampuses(EduOSDbContext context) =>
        context.UserCampusAccesses.AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.UserId == _user.UserId &&
            x.IsActive && !x.IsDeleted).Select(x => x.CampusId);

    private IQueryable<long> AccessibleEnrollmentIds(EduOSDbContext context, long? studentId = null)
    {
        var enrollments = _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted &&
                (!studentId.HasValue || x.StudentId == studentId.Value));
        if (_user.IsTenantAdmin) return enrollments.Select(x => x.Id);
        var campuses = GrantedBillingCampuses(context);
        return enrollments.Where(x => campuses.Contains(x.CampusId)).Select(x => x.Id);
    }

    // A receipt may allocate to several invoices/campuses. For a scoped user, the
    // whole receipt is private unless every allocation is in an authorized campus.
    // No partial receipt total or invoice-number leakage is permitted.
    private IQueryable<long> AccessiblePaymentIds(EduOSDbContext context)
    {
        var payments = _payments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
        // Explicitly fail closed even when IgnoreQueryFilters is used for historical evidence.
        if (!_user.IsAuthenticated || _user.TenantId <= 0 ||
            context.CurrentTenantId != _user.TenantId)
            return payments.Where(x => false).Select(x => x.Id);
        if (_user.IsTenantAdmin) return payments.Select(x => x.Id);

        // A soft-deleted allocation must NEVER make a formerly cross-campus receipt
        // visible to a campus-restricted member. Missing/deleted invoice or enrolment
        // provenance also denies the entire receipt, rather than hiding only one line.
        var allocations = context.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == _user.TenantId);
        var invoices = context.StudentInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == _user.TenantId);
        var enrollments = context.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == _user.TenantId);
        var grants = GrantedBillingCampuses(context);
        return payments.Where(p =>
            allocations.Any(a => a.StudentPaymentId == p.Id) &&
            !allocations.Any(a => a.StudentPaymentId == p.Id &&
                !invoices.Any(i => i.Id == a.StudentInvoiceId && !i.IsDeleted &&
                    enrollments.Any(e => e.Id == i.StudentEnrollmentId &&
                        !e.IsDeleted && e.StudentId == p.StudentId &&
                        grants.Contains(e.CampusId))))).Select(p => p.Id);
    }

    private async Task<bool> CanAccessBillingCampusesAsync(
        IEnumerable<long> campusIds, CancellationToken ct)
    {
        var ids = campusIds.Distinct().ToArray();
        if (ids.Length == 0 || ids.Any(x => x <= 0) ||
            _uow is not EduOSDbContext context || context.CurrentTenantId != _user.TenantId ||
            !_user.IsAuthenticated || _user.TenantId <= 0) return false;
        if (_user.IsTenantAdmin) return true;
        var count = await GrantedBillingCampuses(context)
            .Where(x => ids.Contains(x)).Distinct().CountAsync(ct);
        return count == ids.Length;
    }
}
