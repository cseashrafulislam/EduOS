using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IClassRepository : IGenericRepository<AcademicLevel>
{
    Task<bool> IsClassNameExistsAsync(string name, long tenantId, long? excludeId = null);
    Task<List<AcademicLevel>> GetActiveClassesAsync(long tenantId);
    Task<AcademicLevel?> GetWithSectionsAsync(long id);
    Task<AcademicLevel?> GetWithSubjectsAsync(long id);
}
