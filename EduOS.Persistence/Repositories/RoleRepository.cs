using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class RoleRepository : GenericRepository<Role>, IRoleRepository
    {
        public RoleRepository(EduOSDbContext context) : base(context) { }

        public async Task<Role?> GetWithPermissionsAsync(int id)
        {
            return await _dbSet
                .Include(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<bool> IsRoleNameExistsAsync(string name, int tenantId, int? excludeId = null)
        {
            var query = _dbSet.Where(r => 
                r.Name.ToLower() == name.ToLower() && r.TenantId == tenantId);
            if (excludeId.HasValue)
                query = query.Where(r => r.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<List<Role>> GetActiveRolesAsync(int tenantId)
        {
            return await _dbSet
                .Where(r => r.TenantId == tenantId && r.IsActive)
                .OrderBy(r => r.Name)
                .ToListAsync();
        }
    }
}
