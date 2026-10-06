using EduOS.Core.Entities.Admission;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IAdmissionRepository : IGenericRepository<AdmissionApplicant>
{
    Task<AdmissionApplicant?> GetByApplicationNoAsync(string appNo);
    Task<List<AdmissionApplicant>> GetByStatusAsync(string status, long tenantId);
    Task<List<AdmissionApplicant>> GetByYearAsync(long academicYearId);
    Task<string> GenerateApplicationNoAsync(long tenantId, long academicYearId);
    Task<int> GetCountByStatusAsync(string status, long tenantId);
}
