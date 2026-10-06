using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class GroupRepository : GenericRepository<AcademicTrack>, IGroupRepository
{
    public GroupRepository(EduOSDbContext context) : base(context) { }

    public Task<List<AcademicTrack>> GetActiveGroupsAsync(long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync();

    public async Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null)
    {
        var normalized = code.Trim();
        var query = _dbSet.Where(x => x.TenantId == tenantId && x.Code == normalized);
        if (excludeId.HasValue) query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync();
    }
}
