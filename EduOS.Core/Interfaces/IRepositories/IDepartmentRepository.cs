using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IDepartmentRepository : IGenericRepository<AcademicDepartment>
{
    Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null);
}
