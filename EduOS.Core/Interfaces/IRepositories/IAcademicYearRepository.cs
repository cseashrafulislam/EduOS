using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IAcademicYearRepository : IGenericRepository<AcademicYear>
    {
        Task<AcademicYear?> GetCurrentAsync(long tenantId);
        Task<bool> IsNameExistsAsync(string name, long tenantId, long? excludeId = null);
        Task<List<AcademicYear>> GetActiveYearsAsync(long tenantId);
        Task SetCurrentAsync(long yearId, long tenantId);
    }
}
