using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Canonical academic setup writes. Caller-supplied foreign keys and status must be tenant and campus validated.</summary>
public interface IAcademicSetupService
{
    Task<ApiResponse<AcademicSetupCatalogDto>> GetSetupOptionsAsync(long? academicYearId, string? search, int take = 50, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicProgramDto>> CreateProgramAsync(SaveAcademicProgramRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ProgramCampusAllocationDto>> GetProgramCampusAllocationAsync(long academicProgramId, CancellationToken cancellationToken = default);
    Task<ApiResponse<ProgramCampusAllocationDto>> UpdateProgramCampusAllocationAsync(long academicProgramId, UpdateProgramCampusAllocationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicLevelDto>> CreateLevelAsync(SaveAcademicLevelRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicTrackDto>> CreateTrackAsync(SaveAcademicTrackRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubjectDto>> CreateSubjectAsync(SaveSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCurriculumDto>> CreateCurriculumAsync(SaveAcademicCurriculumRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCurriculumDto>> ActivateCurriculumAsync(long curriculumId, string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<CurriculumSubjectDto>> RegisterCurriculumSubjectAsync(long academicCurriculumId, SaveCurriculumSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicBatchDto>> CreateBatchAsync(SaveAcademicBatchRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<SubjectOfferingDto>>> GetSubjectOfferingsAsync(long academicBatchId, string? search, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubjectOfferingDto>> CreateSubjectOfferingAsync(SaveSubjectOfferingRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubjectOfferingDto>> UpdateSubjectOfferingAsync(Guid reference, UpdateSubjectOfferingRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoomDto>> CreateRoomAsync(SaveRoomRequestDto request, CancellationToken cancellationToken = default);
}
