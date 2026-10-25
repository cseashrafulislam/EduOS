using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;

public interface ILmsAssessmentService
{
    Task<ApiResponse<QuizDto>> SaveQuizAsync(Guid? quizReference, SaveQuizRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<QuizDto>> GetQuizForAuthorAsync(Guid quizReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<QuizCandidateDto>> GetQuizForLearnerAsync(Guid quizReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<QuizAttemptDto>> StartQuizAttemptAsync(StartQuizAttemptRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<QuizAttemptDto>> SubmitQuizAttemptAsync(long attemptId, SubmitQuizAttemptRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<QuizDto>>> GetCourseQuizzesAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<QuizCandidateDto>>> GetAvailableQuizzesAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<long>> GetMyCourseEnrollmentIdAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<QuizAttemptEligibilityDto>> GetMyQuizAttemptEligibilityAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<QuizAttemptDto>> GetMyAttemptAsync(long attemptId, CancellationToken cancellationToken = default);
    Task<ApiResponse<QuizAttemptReviewDto>> GetMyAttemptReviewAsync(long attemptId, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<QuizHistoryRowDto>>> GetMyQuizHistoryAsync(Guid courseReference, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}
