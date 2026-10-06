using EduOS.Core.Entities.Assessment;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class GradeRuleRepository : GenericRepository<GradeRule>, IGradeRuleRepository
{
    public GradeRuleRepository(EduOSDbContext context) : base(context) { }

    public Task<List<GradeRule>> GetByTenantAsync(long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.MaxMarks).ThenBy(x => x.DisplayOrder).ToListAsync();

    public Task<GradeRule?> GetByMarkAsync(decimal mark, long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && mark >= x.MinMarks && mark <= x.MaxMarks)
            .OrderByDescending(x => x.MinMarks).FirstOrDefaultAsync();
}
