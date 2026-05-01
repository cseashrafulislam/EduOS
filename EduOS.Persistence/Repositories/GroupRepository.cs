using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class GroupRepository : GenericRepository<Group>, IGroupRepository
    {
        public GroupRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Group>> GetActiveGroupsAsync(int tenantId)
        {
            return await _dbSet
                .Where(g => g.TenantId == tenantId && g.IsActive)
                .OrderBy(g => g.Name)
                .ToListAsync();
        }

        public async Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null)
        {
            var query = _dbSet.Where(g => 
                g.Code.ToLower() == code.ToLower() && g.TenantId == tenantId);
            if (excludeId.HasValue)
                query = query.Where(g => g.Id != excludeId.Value);
            return await query.AnyAsync();
        }
    }
}
