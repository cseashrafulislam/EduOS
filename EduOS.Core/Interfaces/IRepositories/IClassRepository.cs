using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IClassRepository : IGenericRepository<Class>
    {
        Task<bool> IsClassNameExistsAsync(string name, long tenantId, long? excludeId = null);
        Task<List<Class>> GetActiveClassesAsync(long tenantId);
        Task<Class?> GetWithSectionsAsync(long id);
        Task<Class?> GetWithSubjectsAsync(long id);
    }
}
