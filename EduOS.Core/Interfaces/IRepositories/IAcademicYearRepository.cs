using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IAcademicYearRepository : IGenericRepository<AcademicYear>
    {
        Task<AcademicYear?> GetCurrentAsync(int tenantId);
        Task<bool> IsNameExistsAsync(string name, int tenantId, int? excludeId = null);
        Task<List<AcademicYear>> GetActiveYearsAsync(int tenantId);
        Task SetCurrentAsync(int yearId, int tenantId);
    }
}
