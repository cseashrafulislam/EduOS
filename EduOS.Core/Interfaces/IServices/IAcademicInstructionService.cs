using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Lesson-plan and substitution workflows follow their respective canonical entity state and date semantics.</summary>
public interface IAcademicInstructionService
{
    Task<ApiResponse<IReadOnlyList<SubstitutionDto>>> GetSubstitutionsAsync(DateOnly fromDate, DateOnly toDate, long? academicBatchId, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubstitutionDto>> CreateSubstitutionAsync(CreateSubstitutionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubstitutionDto>> CancelSubstitutionAsync(long id, CancelSubstitutionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LessonPlanDto>>> GetLessonPlansAsync(long? academicBatchId, DateOnly? fromDate, DateOnly? toDate, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> CreateLessonPlanAsync(SaveLessonPlanRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> UpdateLessonPlanAsync(long id, SaveLessonPlanRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> SubmitLessonPlanAsync(long id, string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> ReviewLessonPlanAsync(long id, ReviewLessonPlanRequestDto request, CancellationToken cancellationToken = default);
}
