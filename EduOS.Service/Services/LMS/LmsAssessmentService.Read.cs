using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.LMS;
public sealed partial class LmsAssessmentService
{
    public async Task<ApiResponse<QuizDto>> GetQuizForAuthorAsync(Guid reference, CancellationToken ct = default)
    {
        if (!Author) return Fail<QuizDto>("Author permission required.", 403);
        var quiz = await _quizzes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (quiz == null) return Fail<QuizDto>("Quiz not found.", 404);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == quiz.CourseId && !x.IsDeleted, ct);
        if (course == null || await AuthorCourseAsync(course.PublicId, ct) == null)
            return Fail<QuizDto>("Instructor access denied.", 403);
        return ApiResponse<QuizDto>.SuccessResponse(await MapQuizAsync(quiz, ct));
    }

    public async Task<ApiResponse<IReadOnlyList<QuizDto>>> GetCourseQuizzesAsync(Guid courseReference, CancellationToken ct = default)
    {
        var course = await AuthorCourseAsync(courseReference, ct);
        if (course == null) return Fail<IReadOnlyList<QuizDto>>("Course access denied.", 403);
        var rows = await _quizzes.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CourseId == course.Id && !x.IsDeleted)
            .OrderByDescending(x => x.Id).Take(100).ToListAsync(ct);
        var mapped = new List<QuizDto>();
        foreach (var quiz in rows) mapped.Add(await MapQuizAsync(quiz, ct));
        return ApiResponse<IReadOnlyList<QuizDto>>.SuccessResponse(mapped);
    }

    public async Task<ApiResponse<QuizCandidateDto>> GetQuizForLearnerAsync(Guid reference, CancellationToken ct = default)
    {
        if (!Learner) return Fail<QuizCandidateDto>("Student permission required.", 403);
        var quiz = await _quizzes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && x.IsPublished && !x.IsDeleted, ct);
        if (quiz == null) return Fail<QuizCandidateDto>("Published quiz not found.", 404);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == quiz.CourseId && x.IsActive && !x.IsDeleted, ct);
        var studentId = await StudentIdAsync(ct);
        if (course == null || studentId == 0 || !await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.CourseId == course.Id && x.StudentId == studentId &&
            !x.IsDeleted && (x.State == CourseEnrollmentState.Active ||
            x.State == CourseEnrollmentState.Completed), ct))
            return Fail<QuizCandidateDto>("Course enrollment required.", 403);
        if ((quiz.OpensAt.HasValue && Now < quiz.OpensAt) ||
            (quiz.ClosesAt.HasValue && Now > quiz.ClosesAt))
            return Fail<QuizCandidateDto>("Quiz unavailable at this time.", 409);
        var questions = await _questions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.QuizId == quiz.Id && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var ids = questions.Select(x => x.Id).ToArray();
        var options = await _options.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && ids.Contains(x.QuizQuestionId) && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(ct);
        return ApiResponse<QuizCandidateDto>.SuccessResponse(new QuizCandidateDto
        {
            Reference = quiz.PublicId, Title = quiz.Title, DurationMinutes = quiz.DurationMinutes,
            MaxAttempts = quiz.MaxAttempts, OpensAt = quiz.OpensAt, ClosesAt = quiz.ClosesAt,
            Questions = questions.Select(q => new QuizCandidateQuestionDto
            {
                Id = q.Id, QuestionText = q.QuestionText, QuestionType = q.QuestionType,
                Marks = q.Marks, DisplayOrder = q.DisplayOrder,
                Options = options.Where(o => o.QuizQuestionId == q.Id).Select(o => new QuizCandidateOptionDto
                { Id = o.Id, OptionText = o.OptionText, DisplayOrder = o.DisplayOrder }).ToList()
            }).ToList()
        });
    }

    public async Task<ApiResponse<IReadOnlyList<QuizCandidateDto>>> GetAvailableQuizzesAsync(Guid courseReference, CancellationToken ct = default)
    {
        if (!Learner) return Fail<IReadOnlyList<QuizCandidateDto>>("Student permission required.", 403);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == courseReference && x.IsActive && !x.IsDeleted, ct);
        var studentId = await StudentIdAsync(ct);
        if (course == null || studentId == 0 || !await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.CourseId == course.Id && x.StudentId == studentId &&
            !x.IsDeleted && (x.State == CourseEnrollmentState.Active ||
            x.State == CourseEnrollmentState.Completed), ct))
            return Fail<IReadOnlyList<QuizCandidateDto>>("Course enrollment required.", 403);
        var now = Now;
        var rows = await _quizzes.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted &&
            (!x.OpensAt.HasValue || x.OpensAt <= now) && (!x.ClosesAt.HasValue || x.ClosesAt >= now))
            .OrderBy(x => x.Id).Take(100).Select(x => new QuizCandidateDto
            {
                Reference = x.PublicId, Title = x.Title, DurationMinutes = x.DurationMinutes,
                MaxAttempts = x.MaxAttempts, OpensAt = x.OpensAt, ClosesAt = x.ClosesAt
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<QuizCandidateDto>>.SuccessResponse(rows);
    }
}
