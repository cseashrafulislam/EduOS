using EduOS.Core.Entities.Exams;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class GradeRuleRepository : GenericRepository<GradeRule>, IGradeRuleRepository
    {
        public GradeRuleRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<GradeRule>> GetByTenantAsync(int tenantId)
        {
            return await _dbSet
                .Where(g => g.TenantId == tenantId)
                .OrderByDescending(g => g.MaxMark)
                .ToListAsync();
        }

        public async Task<GradeRule?> GetByMarkAsync(decimal mark, int tenantId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(g => g.TenantId == tenantId 
                    && mark >= g.MinMark 
                    && mark <= g.MaxMark);
        }
    }
}
