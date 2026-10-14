using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.LMS;
public sealed partial class LmsAssessmentService
{
    public async Task<ApiResponse<QuizAttemptReviewDto>> GetMyAttemptReviewAsync(long id, CancellationToken ct = default)
    {
        if (!Learner) return Fail<QuizAttemptReviewDto>("Student permission required.", 403);
        if (id <= 0) return Fail<QuizAttemptReviewDto>("Invalid attempt.");
        var attempt = await _attempts.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && !x.IsDeleted, ct);
        if (attempt == null) return Fail<QuizAttemptReviewDto>("Attempt not found.", 404);
        var quiz = await _quizzes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == attempt.QuizId && !x.IsDeleted, ct);
        if (quiz == null || await EnrollmentAsync(quiz.CourseId, attempt.CourseEnrollmentId, ct) == null)
            return Fail<QuizAttemptReviewDto>("Attempt access denied.", 403);
        if (attempt.State is not (QuizAttemptState.Submitted or QuizAttemptState.Graded))
            return Fail<QuizAttemptReviewDto>("Review is available only after submission.", 409);
        var questions = await _questions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.QuizId == quiz.Id && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var answers = await _answers.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.QuizAttemptId == attempt.Id && !x.IsDeleted).ToListAsync(ct);
        var answerIds = answers.Select(x => x.Id).ToArray();
        var links = await _answerOptions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && answerIds.Contains(x.QuizAnswerId) && !x.IsDeleted).ToListAsync(ct);
        var selectedIds = answers.Where(x => x.SelectedOptionId.HasValue).Select(x => x.SelectedOptionId!.Value)
            .Concat(links.Select(x => x.QuizOptionId)).Distinct().ToArray();
        var options = await _options.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && selectedIds.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
        return ApiResponse<QuizAttemptReviewDto>.SuccessResponse(new QuizAttemptReviewDto
        {
            AttemptId = attempt.Id, QuizReference = quiz.PublicId, ObtainedMarks = attempt.ObtainedMarks,
            Answers = questions.Select(q =>
            {
                var answer = answers.FirstOrDefault(a => a.QuizQuestionId == q.Id);
                var ids = answer == null ? Array.Empty<long>() :
                    answer.SelectedOptionId.HasValue ? new[] { answer.SelectedOptionId.Value } :
                    links.Where(x => x.QuizAnswerId == answer.Id).Select(x => x.QuizOptionId).ToArray();
                return new QuizAttemptAnswerReviewDto
                {
                    QuizQuestionId = q.Id, QuestionText = q.QuestionText, AwardedMarks = answer?.AwardedMarks,
                    SelectedOptions = options.Where(o => ids.Contains(o.Id) && o.QuizQuestionId == q.Id)
                        .OrderBy(o => o.DisplayOrder).Select(o => new QuizAttemptSelectedOptionDto
                        { Id = o.Id, OptionText = o.OptionText }).ToList()
                };
            }).ToList()
        });
    }
}
