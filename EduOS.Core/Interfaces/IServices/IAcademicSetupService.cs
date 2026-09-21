using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

public interface IAcademicSetupService
{
    Task<ApiResponse<AcademicSetupCatalogDto>> GetCatalogAsync(long? academicYearId, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicProgramDto>> CreateProgramAsync(CreateAcademicProgramDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicLevelDto>> CreateLevelAsync(long academicProgramId, CreateAcademicLevelDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicSubjectDto>> CreateSubjectAsync(CreateAcademicSubjectDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCurriculumDto>> CreateCurriculumAsync(CreateAcademicCurriculumDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CurriculumSubjectDto>> RegisterCurriculumSubjectAsync(long academicCurriculumId, RegisterCurriculumSubjectDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicBatchDto>> CreateBatchAsync(CreateAcademicBatchDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicRoomDto>> CreateRoomAsync(CreateAcademicRoomDto request, CancellationToken cancellationToken = default);
}
