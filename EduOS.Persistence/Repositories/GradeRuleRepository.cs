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

    public Task<List<GradeRule>> GetByGradeSchemeAsync(long gradeSchemeId, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.GradeSchemeId == gradeSchemeId)
            .OrderByDescending(x => x.MinMarks).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    public Task<GradeRule?> ResolveGradeAsync(long gradeSchemeId, decimal normalizedMarks, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.GradeSchemeId == gradeSchemeId && normalizedMarks >= x.MinMarks
            && normalizedMarks <= x.MaxMarks).OrderByDescending(x => x.MinMarks)
            .ThenBy(x => x.DisplayOrder).FirstOrDefaultAsync(cancellationToken);
}
