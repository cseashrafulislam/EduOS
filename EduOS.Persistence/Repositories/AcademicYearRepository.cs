using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class AcademicYearRepository : GenericRepository<AcademicYear>, IAcademicYearRepository
    {
        public AcademicYearRepository(EduOSDbContext context) : base(context) { }

        public async Task<AcademicYear?> GetCurrentAsync(long tenantId)
        {
            return await _dbSet.FirstOrDefaultAsync(y => y.TenantId == tenantId && y.IsCurrent);
        }

        public async Task<bool> IsNameExistsAsync(string name, long tenantId, long? excludeId = null)
        {
            var query = _dbSet.Where(y => y.Name.ToLower() == name.ToLower() && y.TenantId == tenantId);
            if (excludeId.HasValue)
                query = query.Where(y => y.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<List<AcademicYear>> GetActiveYearsAsync(long tenantId)
        {
            return await _dbSet.Where(y => y.TenantId == tenantId && y.IsActive).OrderByDescending(y => y.StartDate).ToListAsync();
        }

        public async Task SetCurrentAsync(long yearId, long tenantId)
        {
            var allYears = await _dbSet.Where(y => y.TenantId == tenantId).ToListAsync();
            foreach (var year in allYears)
                year.IsCurrent = year.Id == yearId;
        }
    }
}
