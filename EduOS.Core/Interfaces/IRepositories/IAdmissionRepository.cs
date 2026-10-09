using EduOS.Core.Entities.Admission;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Application numbers are issued through NumberSeries; workflow status is the canonical AdmissionApplicantState enum.</summary>
public interface IAdmissionRepository : IGenericRepository<AdmissionApplicant>
{
    Task<AdmissionApplicant?> GetByApplicationNoAsync(string applicationNumber, CancellationToken cancellationToken = default);
    Task<(List<AdmissionApplicant> Items, int TotalCount)> GetByStateAsync(AdmissionApplicantState state, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<AdmissionApplicant> Items, int TotalCount)> GetByAcademicYearAsync(long academicYearId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> GetCountByStateAsync(AdmissionApplicantState state, CancellationToken cancellationToken = default);
}
