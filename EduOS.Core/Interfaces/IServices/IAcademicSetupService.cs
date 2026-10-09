using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Canonical academic setup writes. Caller-supplied foreign keys and status must be tenant and campus validated.</summary>
public interface IAcademicSetupService
{
    Task<ApiResponse<AcademicSetupCatalogDto>> GetSetupOptionsAsync(long? academicYearId, string? search, int take = 50, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicProgramDto>> CreateProgramAsync(SaveAcademicProgramRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicLevelDto>> CreateLevelAsync(SaveAcademicLevelRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicTrackDto>> CreateTrackAsync(SaveAcademicTrackRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubjectDto>> CreateSubjectAsync(SaveSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCurriculumDto>> CreateCurriculumAsync(SaveAcademicCurriculumRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CurriculumSubjectDto>> RegisterCurriculumSubjectAsync(long academicCurriculumId, SaveCurriculumSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicBatchDto>> CreateBatchAsync(SaveAcademicBatchRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoomDto>> CreateRoomAsync(SaveRoomRequestDto request, CancellationToken cancellationToken = default);
}
