using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

public interface IAcademicInstructionService
{
    Task<ApiResponse<IReadOnlyList<RoutineSubstitutionDto>>> GetSubstitutionsAsync(DateTime fromDate, DateTime toDate, long? academicBatchId, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineSubstitutionDto>> CreateSubstitutionAsync(CreateRoutineSubstitutionDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineSubstitutionDto>> CancelSubstitutionAsync(long id, CancelRoutineSubstitutionDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<LessonPlanDto>>> GetLessonPlansAsync(long? academicBatchId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> CreateLessonPlanAsync(CreateLessonPlanDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> UpdateLessonPlanAsync(long id, UpdateLessonPlanDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> SubmitLessonPlanAsync(long id, AcademicRowVersionDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> ReviewLessonPlanAsync(long id, LessonPlanReviewDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonPlanDto>> RecordLessonProgressAsync(long id, LessonPlanProgressDto request, CancellationToken cancellationToken = default);
}
