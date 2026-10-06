using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IGroupRepository : IGenericRepository<AcademicTrack>
{
    Task<List<AcademicTrack>> GetActiveGroupsAsync(long tenantId);
    Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null);
}
