using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IGroupRepository : IGenericRepository<Group>
    {
        Task<List<Group>> GetActiveGroupsAsync(long tenantId);
        Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null);
    }
}
