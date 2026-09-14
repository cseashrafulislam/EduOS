using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface ISubjectRepository : IGenericRepository<Subject>
    {
        Task<List<Subject>> GetByClassIdAsync(long classId);
        Task<List<Subject>> GetByClassAndGroupAsync(long classId, long? groupId);
        Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null);
    }
}
