using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IDepartmentRepository : IGenericRepository<Department>
    {
        Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null);
    }
}
