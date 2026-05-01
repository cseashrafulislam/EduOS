using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IGroupRepository : IGenericRepository<Group>
    {
        Task<List<Group>> GetActiveGroupsAsync(int tenantId);
        Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null);
    }
}
