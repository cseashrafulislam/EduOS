using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;
public interface ILmsWorkflowService
{
    Task<ApiResponse<LmsCourseDto>> SaveCourseAsync(SaveCourseDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LmsLessonDto>> SaveLessonAsync(SaveLessonDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LmsAssignmentDto>> SaveAssignmentAsync(SaveAssignmentDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<int>> EnrollClassAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<LmsCourseDto>>> GetMyCoursesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<LmsCourseDetailsDto>> GetCourseAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<LmsAssignmentDto>> SubmitAssignmentAsync(SubmitAssignmentDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ReviewSubmissionAsync(ReviewSubmissionDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<decimal>> CompleteLessonAsync(CompleteLessonDto request, CancellationToken cancellationToken = default);
}
