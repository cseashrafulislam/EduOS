using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
namespace EduOS.Service.Services.LMS;
public sealed partial class LmsAssessmentService
{
    public async Task<ApiResponse<QuizAttemptDto>> SubmitQuizAttemptAsync(long id, SubmitQuizAttemptRequestDto request, CancellationToken ct = default)
    {
        if (!Learner) return Fail<QuizAttemptDto>("Student permission required.", 403);
        if (id <= 0 || request?.Answers == null || request.Answers.Count > 100 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.Answers.Any(x => x == null))
            return Fail<QuizAttemptDto>("Invalid submission.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var attempt = await _attempts.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == id && !x.IsDeleted, token);
                if (attempt == null) return Fail<QuizAttemptDto>("Attempt not found.", 404);
                var quiz = await _quizzes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == attempt.QuizId && !x.IsDeleted, token);
                if (quiz == null) return Fail<QuizAttemptDto>("Quiz not found.", 404);
                if (await EnrollmentAsync(quiz.CourseId, attempt.CourseEnrollmentId, token) == null)
                    return Fail<QuizAttemptDto>("Attempt access denied.", 403);
                var questions = await _questions.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == _user.TenantId && x.QuizId == quiz.Id && !x.IsDeleted).ToListAsync(token);
                var questionIds = questions.Select(x => x.Id).ToArray();
                var options = await _options.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == _user.TenantId && questionIds.Contains(x.QuizQuestionId) && !x.IsDeleted)
                    .ToListAsync(token);
                var selections = ValidateSelections(request.Answers, questions, options);
                if (selections == null) return Fail<QuizAttemptDto>("Invalid or duplicate selected options.");
                if (attempt.State is QuizAttemptState.Graded or QuizAttemptState.Submitted)
                    return await SameSubmittedAnswersAsync(attempt.Id, selections, token)
                        ? ApiResponse<QuizAttemptDto>.SuccessResponse(MapAttempt(attempt, quiz.PublicId), "Existing graded attempt.")
                        : Fail<QuizAttemptDto>("Submitted answers are immutable.", 409);
                var courseActive = await _courses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == quiz.CourseId && x.IsActive && !x.IsDeleted, token);
                var enrollmentActive = await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == attempt.CourseEnrollmentId &&
                    x.State == CourseEnrollmentState.Active && !x.IsDeleted, token);
                if (!courseActive || !enrollmentActive)
                    return Fail<QuizAttemptDto>("Completed or archived courses cannot submit new attempts.", 409);
                if (attempt.State != QuizAttemptState.Started ||
                    !VersionMatches(attempt.RowVersion, request.RowVersion))
                    return Fail<QuizAttemptDto>("Attempt changed or expired.", 409);
                if (Now > attempt.StartedAt.AddMinutes(quiz.DurationMinutes) ||
                    (quiz.ClosesAt.HasValue && Now > quiz.ClosesAt))
                    return Fail<QuizAttemptDto>("Attempt time limit exceeded.", 409);
                var total = await PersistSelectedAnswersAsync(attempt.Id, questions, options, selections, token);
                attempt.State = QuizAttemptState.Graded; attempt.SubmittedAt = Now;
                attempt.ObtainedMarks = total; attempt.UpdatedAt = Now; attempt.UpdatedBy = _user.UserId;
                _attempts.Update(attempt);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<QuizAttemptDto>.SuccessResponse(MapAttempt(attempt, quiz.PublicId), "Quiz graded.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<QuizAttemptDto>("Attempt changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Quiz submission conflict tenant {TenantId}", _user.TenantId);
            return Fail<QuizAttemptDto>("Submission conflict.", 409);
        }
    }
}
