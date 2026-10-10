using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Canonical owner of campus, academic-year and academic-term foundation setup.</summary>
public interface IInstitutionFoundationService
{
    Task<ApiResponse<IReadOnlyList<CampusDto>>> GetCampusesAsync(CancellationToken ct = default);
    Task<ApiResponse<CampusDto>> GetCampusAsync(long id, CancellationToken ct = default);
    Task<ApiResponse<CampusDto>> SaveCampusAsync(long? id, SaveCampusRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> ArchiveCampusAsync(long id, string rowVersion, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<AcademicYearDto>>> GetAcademicYearsAsync(CancellationToken ct = default);
    Task<ApiResponse<AcademicYearDto>> GetAcademicYearAsync(long id, CancellationToken ct = default);
    Task<ApiResponse<AcademicYearDto>> SaveAcademicYearAsync(long? id, SaveAcademicYearRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> ArchiveAcademicYearAsync(long id, string rowVersion, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<AcademicTermDto>>> GetAcademicTermsAsync(long? academicYearId = null, CancellationToken ct = default);
    Task<ApiResponse<AcademicTermDto>> GetAcademicTermAsync(long id, CancellationToken ct = default);
    Task<ApiResponse<AcademicTermDto>> SaveAcademicTermAsync(long? id, SaveAcademicTermRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> ArchiveAcademicTermAsync(long id, string rowVersion, CancellationToken ct = default);
}
