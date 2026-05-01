using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IClassRepository : IGenericRepository<Class>
    {
        Task<bool> IsClassNameExistsAsync(string name, int tenantId, int? excludeId = null);
        Task<List<Class>> GetActiveClassesAsync(int tenantId);
        Task<Class?> GetWithSectionsAsync(int id);
        Task<Class?> GetWithSubjectsAsync(int id);
    }
}
