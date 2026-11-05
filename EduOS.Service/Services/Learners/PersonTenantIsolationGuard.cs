using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Learners;

/// <summary>
/// Protects global Person data from tenant-local mutation when another institution
/// has an identity relationship, including archived or historical relationships.
/// This is not a permission to read the other institution's records.
/// </summary>
public sealed class PersonTenantIsolationGuard(
    IGenericRepository<StudentPersonLink> links,
    IGenericRepository<Student> students,
    IGenericRepository<Guardian> guardians,
    IGenericRepository<Employee> employees,
    IGenericRepository<AdmissionApplicant> applicants,
    IGenericRepository<TenantMembership> memberships,
    IGenericRepository<ApplicationUser> identityUsers,
    IGenericRepository<LearnerConsentRequest> consents,
    IGenericRepository<LearnerDataGrant> grants)
{
    public async Task<bool> HasOtherTenantAssociationAsync(long personId, long tenantId, CancellationToken ct = default)
    {
        if (personId <= 0 || tenantId <= 0) return true;
        return await links.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != tenantId, ct)
            || await students.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != tenantId, ct)
            || await guardians.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != tenantId, ct)
            || await employees.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != tenantId, ct)
            || await applicants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != tenantId, ct)
            || await memberships.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.TenantId != tenantId && identityUsers.GetQueryable()
                    .IgnoreQueryFilters().Any(u => u.Id == x.UserId && u.PersonId == personId), ct)
            || await consents.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != tenantId, ct)
            || await grants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != tenantId, ct);
    }
}
