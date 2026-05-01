using EduOS.Core.Entities.Auth;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IPermissionRepository : IGenericRepository<Permission>
    {
        Task<List<Permission>> GetByRoleIdAsync(int roleId);
        Task<List<Permission>> GetByModuleAsync(string module);
        Task<Permission?> GetByNameAsync(string name);
    }
}
