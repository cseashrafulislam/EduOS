using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
namespace EduOS.Service.Services.LMS;
public sealed partial class LmsAssessmentService
{
    public async Task<ApiResponse<QuizDto>> SaveQuizAsync(Guid? reference, SaveQuizRequestDto request, CancellationToken ct = default)
    {
        if (!Author) return Fail<QuizDto>("Author permission required.", 403);
        if (request == null || request.CourseReference == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 200 || request.DurationMinutes is < 1 or > 1440 ||
            request.MaxAttempts is < 1 or > 100 || request.Questions == null ||
            request.Questions.Count is < 1 or > 50 || (request.OpensAt.HasValue &&
            request.ClosesAt.HasValue && request.OpensAt >= request.ClosesAt))
            return Fail<QuizDto>("Invalid quiz metadata.");
        if (request.Questions.Any(q => q == null || string.IsNullOrWhiteSpace(q.QuestionText) ||
            q.QuestionText.Trim().Length > 4000 ||
            (q.QuestionType != "SingleChoice" && q.QuestionType != "MultipleChoice") ||
            q.Marks <= 0 || q.Options == null || q.Options.Count is < 2 or > 10 ||
            q.Options.Any(o => o == null || string.IsNullOrWhiteSpace(o.OptionText) ||
                o.OptionText.Trim().Length > 2000) ||
            (q.QuestionType == "SingleChoice"
                ? q.Options.Count(o => o.IsCorrect) != 1
                : q.Options.Count(o => o.IsCorrect) < 1 || q.Options.All(o => o.IsCorrect)) ||
            q.Options.Select(o => o.OptionText.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != q.Options.Count))
            return Fail<QuizDto>("Questions must have one correct option and 2-10 valid options.");
        var total = request.Questions.Sum(q => q.Marks);
        if (total > 100000 || request.PassMarks < 0 || request.PassMarks > total)
            return Fail<QuizDto>("Invalid pass marks.");
        var course = await AuthorCourseAsync(request.CourseReference, ct);
        if (course == null) return Fail<QuizDto>("Course access denied.", 403);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                if (await CourseCompletionPublicationGuard.IsLockedAsync(
                    _enrollments, _user.TenantId, course.Id, token))
                    return Fail<QuizDto>(CourseCompletionPublicationGuard.Message, 409);
                var quiz = reference.HasValue ? await _quizzes.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.PublicId == reference.Value &&
                    x.CourseId == course.Id && !x.IsDeleted, token) : null;
                if (reference.HasValue && quiz == null) return Fail<QuizDto>("Quiz not found.", 404);
                if (quiz != null && (quiz.IsPublished || !VersionMatches(quiz.RowVersion, request.RowVersion)))
                    return Fail<QuizDto>("Published quiz is immutable or row version changed.", 409);
                if (await _quizzes.GetQueryable().AnyAsync(x => x.TenantId == _user.TenantId &&
                    x.CourseId == course.Id && x.Title == request.Title.Trim() && !x.IsDeleted &&
                    (quiz == null || x.Id != quiz.Id), token))
                    return Fail<QuizDto>("Quiz title already exists.", 409);
                if (quiz == null)
                {
                    quiz = new Quiz { TenantId = _user.TenantId, CourseId = course.Id,
                        PublicId = Guid.NewGuid(), Title = request.Title.Trim(), DurationMinutes = request.DurationMinutes,
                        PassMarks = request.PassMarks, MaxAttempts = request.MaxAttempts,
                        OpensAt = request.OpensAt, ClosesAt = request.ClosesAt,
                        CreatedAt = Now, CreatedBy = _user.UserId };
                    await _quizzes.AddAsync(quiz);
                    await _uow.SaveChangesAsync(token);
                }
                else
                {
                    var old = await _questions.GetQueryable().Where(x => x.TenantId == _user.TenantId &&
                        x.QuizId == quiz.Id && !x.IsDeleted).ToListAsync(token);
                    var ids = old.Select(x => x.Id).ToArray();
                    var opts = await _options.GetQueryable().Where(x => x.TenantId == _user.TenantId &&
                        ids.Contains(x.QuizQuestionId) && !x.IsDeleted).ToListAsync(token);
                    foreach (var q in old) { q.IsDeleted = true; q.DeletedAt = Now; q.DeletedBy = _user.UserId; _questions.Update(q); }
                    foreach (var o in opts) { o.IsDeleted = true; o.DeletedAt = Now; o.DeletedBy = _user.UserId; _options.Update(o); }
                    quiz.UpdatedAt = Now; quiz.UpdatedBy = _user.UserId; _quizzes.Update(quiz);
                }
                quiz.Title = request.Title.Trim(); quiz.DurationMinutes = request.DurationMinutes;
                quiz.PassMarks = request.PassMarks; quiz.MaxAttempts = request.MaxAttempts;
                quiz.OpensAt = request.OpensAt; quiz.ClosesAt = request.ClosesAt;
                quiz.IsPublished = request.IsPublished;
                var order = 0;
                foreach (var input in request.Questions)
                {
                    var q = new QuizQuestion { TenantId = _user.TenantId, QuizId = quiz.Id,
                        QuestionText = input.QuestionText.Trim(), QuestionType = input.QuestionType,
                        Marks = input.Marks, DisplayOrder = ++order, CreatedAt = Now, CreatedBy = _user.UserId };
                    await _questions.AddAsync(q);
                    await _uow.SaveChangesAsync(token);
                    var n = 0;
                    foreach (var o in input.Options)
                        await _options.AddAsync(new QuizOption { TenantId = _user.TenantId,
                            QuizQuestionId = q.Id, OptionText = o.OptionText.Trim(),
                            IsCorrect = o.IsCorrect, DisplayOrder = ++n, CreatedAt = Now, CreatedBy = _user.UserId });
                }
                await _uow.SaveChangesAsync(token);
                return ApiResponse<QuizDto>.SuccessResponse(await MapQuizAsync(quiz, token), "Quiz saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<QuizDto>("Concurrent quiz update.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Quiz write conflict tenant {TenantId}", _user.TenantId);
            return Fail<QuizDto>("Quiz write conflict.", 409);
        }
    }
}
