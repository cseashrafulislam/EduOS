using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class PermissionRepository : GenericRepository<Permission>, IPermissionRepository
{
    public PermissionRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<Permission>> GetByRoleIdAsync(long roleId)
    {
        var permissionIds = _context.RolePermissions.Where(x => x.RoleId == roleId && x.IsAllowed)
            .Select(x => x.PermissionId);
        return await _dbSet.AsNoTracking().Where(x => permissionIds.Contains(x.Id) && x.IsActive)
            .OrderBy(x => x.ModuleCode).ThenBy(x => x.Name).ToListAsync();
    }

    public Task<List<Permission>> GetByModuleAsync(string module) =>
        _dbSet.AsNoTracking().Where(x => x.ModuleCode == module && x.IsActive)
            .OrderBy(x => x.Name).ToListAsync();

    public Task<Permission?> GetByNameAsync(string name) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Name == name);
}
