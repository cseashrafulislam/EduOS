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
    Task<ApiResponse<QuizAttemptDto>> GetMyAttemptAsync(long attemptId, CancellationToken cancellationToken = default);
}
