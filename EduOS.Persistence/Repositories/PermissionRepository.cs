using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class PermissionRepository : GenericRepository<Permission>, IPermissionRepository
    {
        public PermissionRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Permission>> GetByRoleIdAsync(int roleId)
        {
            return await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => rp.Permission!)
                .ToListAsync();
        }

        public async Task<List<Permission>> GetByModuleAsync(string module)
        {
            return await _dbSet
                .Where(p => p.Module == module)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        public async Task<Permission?> GetByNameAsync(string name)
        {
            return await _dbSet.FirstOrDefaultAsync(p => p.Name == name);
        }
    }
}
