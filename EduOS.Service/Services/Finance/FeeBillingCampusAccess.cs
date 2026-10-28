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

    private IQueryable<long> AccessibleEnrollmentIds(EduOSDbContext context)
    {
        var enrollments = _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
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
        if (_user.IsTenantAdmin) return payments.Select(x => x.Id);
        var accessible = AccessibleEnrollmentIds(context);
        var allocations = _allocations.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
        var invoices = _invoices.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
        return payments.Where(p =>
            allocations.Any(a => a.StudentPaymentId == p.Id) &&
            !allocations.Any(a => a.StudentPaymentId == p.Id &&
                !invoices.Any(i => i.Id == a.StudentInvoiceId &&
                    accessible.Contains(i.StudentEnrollmentId)))).Select(p => p.Id);
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
