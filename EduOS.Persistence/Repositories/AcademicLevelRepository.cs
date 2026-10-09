using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class AcademicLevelRepository : GenericRepository<AcademicLevel>, IAcademicLevelRepository
{
    public AcademicLevelRepository(EduOSDbContext context) : base(context) { }

    public async Task<bool> IsLevelNameExistsAsync(string name, long tenantId, long? excludeId = null)
    {
        var normalized = name.Trim();
        var query = _dbSet.Where(x => x.TenantId == tenantId && x.Name == normalized);
        if (excludeId.HasValue) query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    public Task<List<AcademicLevel>> GetActiveLevelsAsync(long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.LevelNo).ThenBy(x => x.Name).ToListAsync();

public Task<AcademicLevel?> GetWithSubjectsAsync(long id) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
}
