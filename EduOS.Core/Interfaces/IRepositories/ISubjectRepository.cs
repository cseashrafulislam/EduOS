using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface ISubjectRepository : IGenericRepository<Subject>
    {
        Task<List<Subject>> GetByClassIdAsync(int classId);
        Task<List<Subject>> GetByClassAndGroupAsync(int classId, int? groupId);
        Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null);
    }
}
