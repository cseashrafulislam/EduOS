using EduOS.Core.Entities.Auth;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IRoleRepository : IGenericRepository<Role>
    {
        Task<Role?> GetWithPermissionsAsync(int id);
        Task<bool> IsRoleNameExistsAsync(string name, int tenantId, int? excludeId = null);
        Task<List<Role>> GetActiveRolesAsync(int tenantId);
    }
}
