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
    public async Task<ApiResponse<QuizAttemptDto>> StartQuizAttemptAsync(StartQuizAttemptRequestDto request, CancellationToken ct = default)
    {
        if (!Learner) return Fail<QuizAttemptDto>("Student permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.QuizReference == Guid.Empty ||
            request.CourseEnrollmentId <= 0) return Fail<QuizAttemptDto>("Invalid attempt request.");
        var quiz = await _quizzes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == request.QuizReference && x.IsPublished && !x.IsDeleted, ct);
        if (quiz == null) return Fail<QuizAttemptDto>("Published quiz not found.", 404);
        var enrollment = await EnrollmentAsync(quiz.CourseId, request.CourseEnrollmentId, ct);
        if (enrollment == null) return Fail<QuizAttemptDto>("Course enrollment required.", 403);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var prior = await _attempts.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (prior != null)
                    return prior.QuizId == quiz.Id && prior.CourseEnrollmentId == request.CourseEnrollmentId
                        ? ApiResponse<QuizAttemptDto>.SuccessResponse(MapAttempt(prior, quiz.PublicId), "Existing attempt.")
                        : Fail<QuizAttemptDto>("Request ID already used.", 409);
                var courseActive = await _courses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == quiz.CourseId && x.IsActive && !x.IsDeleted, token);
                var enrollmentActive = await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == enrollment.Id && !x.IsDeleted &&
                    x.State == CourseEnrollmentState.Active, token);
                if (!courseActive || !enrollmentActive)
                    return Fail<QuizAttemptDto>("Completed or archived courses cannot start new attempts.", 409);
                if ((quiz.OpensAt.HasValue && Now < quiz.OpensAt) ||
                    (quiz.ClosesAt.HasValue && Now > quiz.ClosesAt))
                    return Fail<QuizAttemptDto>("Quiz unavailable at this time.", 409);
                if (await _attempts.GetQueryable().AnyAsync(x => x.TenantId == _user.TenantId &&
                    x.QuizId == quiz.Id && x.CourseEnrollmentId == request.CourseEnrollmentId &&
                    !x.IsDeleted && x.State == QuizAttemptState.Started, token))
                    return Fail<QuizAttemptDto>("Finish existing attempt first.", 409);
                var count = await _attempts.GetQueryable().CountAsync(x => x.TenantId == _user.TenantId &&
                    x.QuizId == quiz.Id && x.CourseEnrollmentId == request.CourseEnrollmentId && !x.IsDeleted, token);
                if (count >= quiz.MaxAttempts) return Fail<QuizAttemptDto>("Attempt limit reached.", 409);
                var row = new QuizAttempt { TenantId = _user.TenantId, QuizId = quiz.Id,
                    CourseEnrollmentId = request.CourseEnrollmentId, ClientRequestId = request.ClientRequestId,
                    AttemptNo = count + 1, State = QuizAttemptState.Started, StartedAt = Now,
                    CreatedAt = Now, CreatedBy = _user.UserId };
                await _attempts.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<QuizAttemptDto>.SuccessResponse(MapAttempt(row, quiz.PublicId), "Attempt started.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Quiz attempt conflict tenant {TenantId}", _user.TenantId);
            return Fail<QuizAttemptDto>("Attempt conflict.", 409);
        }
    }

    public async Task<ApiResponse<QuizAttemptDto>> GetMyAttemptAsync(long id, CancellationToken ct = default)
    {
        if (!Learner) return Fail<QuizAttemptDto>("Student permission required.", 403);
        var attempt = await _attempts.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && !x.IsDeleted, ct);
        if (attempt == null) return Fail<QuizAttemptDto>("Attempt not found.", 404);
        var quiz = await _quizzes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == attempt.QuizId && !x.IsDeleted, ct);
        if (quiz == null || await EnrollmentAsync(quiz.CourseId, attempt.CourseEnrollmentId, ct) == null ||
            !await CanReadArchivedAttemptAsync(quiz.CourseId, attempt.CourseEnrollmentId, ct))
            return Fail<QuizAttemptDto>("Attempt access denied.", 403);
        return ApiResponse<QuizAttemptDto>.SuccessResponse(MapAttempt(attempt, quiz.PublicId));
    }
}
