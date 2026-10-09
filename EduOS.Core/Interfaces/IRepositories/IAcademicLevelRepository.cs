using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IAcademicLevelRepository : IGenericRepository<AcademicLevel>
{
    Task<bool> IsLevelNameExistsAsync(string name, long tenantId, long? excludeId = null);
    Task<List<AcademicLevel>> GetActiveLevelsAsync(long tenantId);
}
