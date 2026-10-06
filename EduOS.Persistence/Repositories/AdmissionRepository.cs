using EduOS.Core.Entities.Admission;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class AdmissionRepository : GenericRepository<AdmissionApplicant>, IAdmissionRepository
{
    public AdmissionRepository(EduOSDbContext context) : base(context) { }

    public Task<AdmissionApplicant?> GetByApplicationNoAsync(string appNo) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.ApplicationNumber == appNo);

    public async Task<List<AdmissionApplicant>> GetByStatusAsync(string status, long tenantId)
    {
        if (!Enum.TryParse<AdmissionApplicantState>(status, true, out var state))
            return new List<AdmissionApplicant>();
        return await _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.State == state)
            .OrderByDescending(x => x.SubmittedAt ?? x.CreatedAt).ToListAsync();
    }

    public async Task<List<AdmissionApplicant>> GetByYearAsync(long academicYearId)
    {
        var intakeIds = _context.Set<AdmissionIntakeForm>().Where(x => x.AcademicYearId == academicYearId).Select(x => x.Id);
        return await _dbSet.AsNoTracking().Where(x => intakeIds.Contains(x.AdmissionIntakeFormId))
            .OrderByDescending(x => x.SubmittedAt ?? x.CreatedAt).ToListAsync();
    }

    public Task<string> GenerateApplicationNoAsync(long tenantId, long academicYearId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        return Task.FromResult($"APP-{academicYearId}-{suffix}");
    }

    public async Task<int> GetCountByStatusAsync(string status, long tenantId)
    {
        if (!Enum.TryParse<AdmissionApplicantState>(status, true, out var state))
            return 0;
        return await _dbSet.CountAsync(x => x.TenantId == tenantId && x.State == state);
    }
}
