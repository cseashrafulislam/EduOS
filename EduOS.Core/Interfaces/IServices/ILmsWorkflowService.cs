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
    Task<ApiResponse<PagedResult<CourseDto>>> GetMyCoursesAsync(int page, int pageSize, CancellationToken cancellationToken = default, bool includeArchived = false);
    Task<ApiResponse<CourseDetailsDto>> GetCourseDetailsAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<CourseContentPageDto>> GetCourseContentPageAsync(Guid courseReference, int lessonPage, int assignmentPage, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<MyAssignmentWorkDto>>> GetMyAssignmentWorkAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AssignmentSubmissionFileDto>>> GetSubmissionFilesAsync(long submissionId, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssignmentSubmissionDto>> SubmitAssignmentAsync(SubmitAssignmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<AssignmentSubmissionDto>>> GetAssignmentSubmissionsAsync(Guid assignmentReference, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssignmentSubmissionDto>> GradeSubmissionAsync(long submissionId, GradeAssignmentSubmissionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CourseAuthoringLockDto>> GetCourseAuthoringLockAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<CourseCompletionRegisterRowDto>>> GetCourseCompletionRegisterAsync(Guid courseReference, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<CourseCompletionRegisterRowDto>>> GetCourseCompletionExportAsync(Guid courseReference, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<CourseCompletionRegisterRowDto>> CompleteCourseEnrollmentAsync(long enrollmentId, CompleteCourseEnrollmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LmsCertificateDto>> IssueCertificateAsync(IssueLmsCertificateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LmsCertificateDto>> RevokeCertificateAsync(Guid reference, RevokeLmsCertificateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LmsCertificateDto>> GetCertificateAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LmsCertificateDto>>> GetMyCertificatesAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LmsCertificateDto>>> GetCourseCertificatesAsync(Guid courseReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<LmsCertificateVerificationDto>> VerifyCertificateAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LmsLearnerCourseProgressDto>>> GetMyCourseProgressAsync(int page, int pageSize, CancellationToken cancellationToken = default, bool includeArchived = false);
    Task<ApiResponse<PagedResult<LessonProgressDto>>> GetMyLessonProgressAsync(Guid courseReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<LessonProgressDto>> UpdateLessonProgressAsync(UpdateLessonProgressRequestDto request, CancellationToken cancellationToken = default);
}
