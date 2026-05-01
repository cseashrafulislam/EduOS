using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IAdmissionRepository : IGenericRepository<Admission>
    {
        Task<Admission?> GetByApplicationNoAsync(string appNo);
        Task<List<Admission>> GetByStatusAsync(string status, int tenantId);
        Task<List<Admission>> GetByYearAsync(int academicYearId);
        Task<string> GenerateApplicationNoAsync(int tenantId, int academicYearId);
        Task<int> GetCountByStatusAsync(string status, int tenantId);
    }
}
