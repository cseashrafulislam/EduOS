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
        if (id <= 0 || request == null || request.Answers == null || request.Answers.Count > 100 ||
            string.IsNullOrWhiteSpace(request.RowVersion)) return Fail<QuizAttemptDto>("Invalid submission.");
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
                if (attempt.State is QuizAttemptState.Graded or QuizAttemptState.Submitted)
                    return ApiResponse<QuizAttemptDto>.SuccessResponse(MapAttempt(attempt, quiz.PublicId), "Already submitted.");
                if (attempt.State != QuizAttemptState.Started ||
                    !VersionMatches(attempt.RowVersion, request.RowVersion))
                    return Fail<QuizAttemptDto>("Attempt changed or expired.", 409);
                if (Now > attempt.StartedAt.AddMinutes(quiz.DurationMinutes) ||
                    (quiz.ClosesAt.HasValue && Now > quiz.ClosesAt))
                    return Fail<QuizAttemptDto>("Attempt time limit exceeded.", 409);
                var questions = await _questions.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == _user.TenantId && x.QuizId == quiz.Id && !x.IsDeleted).ToListAsync(token);
                var ids = questions.Select(x => x.Id).ToArray();
                var options = await _options.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == _user.TenantId && ids.Contains(x.QuizQuestionId) && !x.IsDeleted).ToListAsync(token);
                if (request.Answers.GroupBy(x => x.QuizQuestionId).Any(g => g.Count() > 1) ||
                    request.Answers.Any(x => !ids.Contains(x.QuizQuestionId) || x.AnswerText != null ||
                        (x.SelectedOptionId.HasValue && !options.Any(o =>
                            o.Id == x.SelectedOptionId && o.QuizQuestionId == x.QuizQuestionId))))
                    return Fail<QuizAttemptDto>("Answer does not belong to this quiz.");
                decimal total = 0;
                foreach (var answer in request.Answers)
                {
                    var q = questions.First(x => x.Id == answer.QuizQuestionId);
                    var correct = answer.SelectedOptionId.HasValue &&
                        options.Any(o => o.Id == answer.SelectedOptionId && o.IsCorrect);
                    var marks = correct ? q.Marks : 0m;
                    total += marks;
                    await _answers.AddAsync(new QuizAnswer { TenantId = _user.TenantId,
                        QuizAttemptId = attempt.Id, QuizQuestionId = q.Id,
                        SelectedOptionId = answer.SelectedOptionId, AwardedMarks = marks,
                        CreatedAt = Now, CreatedBy = _user.UserId });
                }
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
