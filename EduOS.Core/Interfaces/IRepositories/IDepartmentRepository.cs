using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IDepartmentRepository : IGenericRepository<Department>
    {
        Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null);
    }
}
