using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class AdmissionRepository : GenericRepository<Admission>, IAdmissionRepository
    {
        public AdmissionRepository(EduOSDbContext context) : base(context) { }

        public async Task<Admission?> GetByApplicationNoAsync(string appNo)
        {
            return await _dbSet
                .Include(a => a.Class)
                .Include(a => a.AcademicYear)
                .FirstOrDefaultAsync(a => a.ApplicationNo == appNo);
        }

        public async Task<List<Admission>> GetByStatusAsync(string status, int tenantId)
        {
            return await _dbSet
                .Where(a => a.Status == status && a.TenantId == tenantId)
                .OrderByDescending(a => a.ApplicationDate)
                .ToListAsync();
        }

        public async Task<List<Admission>> GetByYearAsync(int academicYearId)
        {
            return await _dbSet
                .Include(a => a.Class)
                .Where(a => a.AcademicYearId == academicYearId)
                .OrderByDescending(a => a.ApplicationDate)
                .ToListAsync();
        }

        public async Task<string> GenerateApplicationNoAsync(int tenantId, int academicYearId)
        {
            var count = await _dbSet
                .CountAsync(a => a.TenantId == tenantId && a.AcademicYearId == academicYearId);
            return $"APP{(count + 1):D5}";
        }

        public async Task<int> GetCountByStatusAsync(string status, int tenantId)
        {
            return await _dbSet
                .CountAsync(a => a.Status == status && a.TenantId == tenantId);
        }
    }
}
