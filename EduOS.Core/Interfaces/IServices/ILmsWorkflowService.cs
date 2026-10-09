using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>
/// Canonical Course/Lesson/Assignment workflows. Eligibility is resolved from server-side
/// subject offerings and enrollment, never from redundant class/section fields on Course.
/// </summary>
public interface ILmsWorkflowService
{
    Task<ApiResponse<CourseDto>> SaveCourseAsync(Guid? courseReference, SaveCourseRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonDto>> SaveLessonAsync(Guid? lessonReference, SaveLessonRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssignmentDto>> SaveAssignmentAsync(Guid? assignmentReference, SaveAssignmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CourseEnrollmentDto>> EnrollStudentAsync(EnrollCourseRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AcademicInstructorChoiceDto>>> SearchInstructorsAsync(string search, int take = 20, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<CourseDto>>> GetMyCoursesAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<CourseDetailsDto>> GetCourseDetailsAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssignmentSubmissionDto>> SubmitAssignmentAsync(SubmitAssignmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssignmentSubmissionDto>> GradeSubmissionAsync(long submissionId, GradeAssignmentSubmissionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonProgressDto>> UpdateLessonProgressAsync(UpdateLessonProgressRequestDto request, CancellationToken cancellationToken = default);
}
