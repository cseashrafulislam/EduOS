using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IAdmissionRepository : IGenericRepository<Admission>
    {
        Task<Admission?> GetByApplicationNoAsync(string appNo);
        Task<List<Admission>> GetByStatusAsync(string status, long tenantId);
        Task<List<Admission>> GetByYearAsync(long academicYearId);
        Task<string> GenerateApplicationNoAsync(long tenantId, long academicYearId);
        Task<int> GetCountByStatusAsync(string status, long tenantId);
    }
}
