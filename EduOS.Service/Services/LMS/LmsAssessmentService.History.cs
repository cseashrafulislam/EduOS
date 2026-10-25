using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsAssessmentService
{
    // Archived evidence requires the original academic enrollment and historical
    // curriculum/campus dates. Missing repositories fail closed, never broaden access.
    private async Task<long[]> HistoricalEnrollmentIdsAsync(long courseId, long studentId, CancellationToken ct)
    {
        if (studentId <= 0 || _historyAcademics == null || _historyBatches == null ||
            _historyYears == null || _historyCurricula == null || _historyTerms == null ||
            _historySubjects == null) return Array.Empty<long>();
        var tenant = _user.TenantId;
        var now = Now;
        var query =
            from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join course in _courses.GetQueryable().AsNoTracking() on enrollment.CourseId equals course.Id
            join academic in _historyAcademics.GetQueryable().AsNoTracking()
                on enrollment.StudentEnrollmentId equals (long?)academic.Id
            join batch in _historyBatches.GetQueryable().AsNoTracking()
                on academic.AcademicBatchId equals batch.Id
            join year in _historyYears.GetQueryable().AsNoTracking()
                on academic.AcademicYearId equals year.Id
            join curriculum in _historyCurricula.GetQueryable().AsNoTracking()
                on academic.AcademicCurriculumId equals curriculum.Id
            where enrollment.TenantId == tenant && course.TenantId == tenant &&
                academic.TenantId == tenant && batch.TenantId == tenant &&
                year.TenantId == tenant && curriculum.TenantId == tenant &&
                !enrollment.IsDeleted && !course.IsDeleted && !academic.IsDeleted &&
                !batch.IsDeleted && !year.IsDeleted && !curriculum.IsDeleted &&
                course.Id == courseId && !course.IsActive &&
                enrollment.StudentId == studentId && academic.StudentId == studentId &&
                enrollment.State == CourseEnrollmentState.Completed &&
                enrollment.CompletedAt.HasValue && enrollment.CompletedAt.Value <= now &&
                enrollment.CompletedAt.Value >= enrollment.EnrolledAt &&
                (academic.State == EnrollmentState.Completed ||
                 academic.State == EnrollmentState.Promoted ||
                 academic.State == EnrollmentState.Active) &&
                academic.EnrollmentDate >= year.StartDate && academic.EnrollmentDate <= year.EndDate &&
                (!academic.EndDate.HasValue || academic.EndDate.Value >= academic.EnrollmentDate) &&
                batch.CampusId == academic.CampusId &&
                batch.AcademicYearId == academic.AcademicYearId &&
                batch.AcademicProgramId == academic.AcademicProgramId &&
                batch.AcademicLevelId == academic.AcademicLevelId &&
                (!batch.StartDate.HasValue || batch.StartDate.Value <= academic.EnrollmentDate) &&
                (!batch.EndDate.HasValue || batch.EndDate.Value >= academic.EnrollmentDate) &&
                (!year.CampusId.HasValue || year.CampusId == academic.CampusId) &&
                curriculum.AcademicProgramId == academic.AcademicProgramId &&
                curriculum.EffectiveFrom <= academic.EnrollmentDate &&
                (!curriculum.EffectiveTo.HasValue || curriculum.EffectiveTo.Value >= academic.EnrollmentDate) &&
                (!course.AcademicProgramId.HasValue ||
                 course.AcademicProgramId.Value == academic.AcademicProgramId) &&
                (!academic.AcademicTermId.HasValue ||
                 _historyTerms.GetQueryable().Any(term =>
                    term.TenantId == tenant && !term.IsDeleted &&
                    term.Id == academic.AcademicTermId.Value &&
                    term.AcademicYearId == year.Id &&
                    term.StartDate <= year.EndDate && term.EndDate >= year.StartDate)) &&
                (!course.SubjectId.HasValue ||
                 _historySubjects.GetQueryable().Any(subject =>
                    subject.TenantId == tenant && !subject.IsDeleted &&
                    subject.AcademicCurriculumId == curriculum.Id &&
                    subject.AcademicLevelId == academic.AcademicLevelId &&
                    subject.SubjectId == course.SubjectId.Value))
            select enrollment.Id;
        return await query.Distinct().ToArrayAsync(ct);
    }

    private async Task<bool> CanReadArchivedAttemptAsync(long courseId, long enrollmentId, CancellationToken ct)
    {
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == courseId && !x.IsDeleted, ct);
        if (course == null) return false;
        if (course.IsActive) return true;
        var studentId = await StudentIdAsync(ct);
        return (await HistoricalEnrollmentIdsAsync(courseId, studentId, ct)).Contains(enrollmentId);
    }

    public async Task<ApiResponse<PagedResult<QuizHistoryRowDto>>> GetMyQuizHistoryAsync(
        Guid courseReference, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!Learner) return Fail<PagedResult<QuizHistoryRowDto>>("Student permission required.", 403);
        if (courseReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Fail<PagedResult<QuizHistoryRowDto>>("Invalid history search.");
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == courseReference && !x.IsDeleted, cancellationToken);
        var studentId = await StudentIdAsync(cancellationToken);
        if (course == null || studentId == 0)
            return Fail<PagedResult<QuizHistoryRowDto>>("Course history not found.", 404);
        var allowedIds = course.IsActive
            ? await _enrollments.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == _user.TenantId && x.CourseId == course.Id && x.StudentId == studentId &&
                !x.IsDeleted && (x.State == CourseEnrollmentState.Active ||
                                 x.State == CourseEnrollmentState.Completed))
                .Select(x => x.Id).ToArrayAsync(cancellationToken)
            : await HistoricalEnrollmentIdsAsync(course.Id, studentId, cancellationToken);
        if (allowedIds.Length == 0)
            return Fail<PagedResult<QuizHistoryRowDto>>("Course history not found.", 404);
        var query =
            from attempt in _attempts.GetQueryable().AsNoTracking()
            join quiz in _quizzes.GetQueryable().AsNoTracking() on attempt.QuizId equals quiz.Id
            where attempt.TenantId == _user.TenantId && quiz.TenantId == _user.TenantId &&
                quiz.CourseId == course.Id && !attempt.IsDeleted && !quiz.IsDeleted &&
                allowedIds.Contains(attempt.CourseEnrollmentId) &&
                (attempt.State == QuizAttemptState.Graded || attempt.State == QuizAttemptState.Submitted) &&
                attempt.SubmittedAt.HasValue
            select new { attempt, quiz };
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.attempt.SubmittedAt)
            .ThenByDescending(x => x.attempt.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new QuizHistoryRowDto
            {
                AttemptId = x.attempt.Id, QuizReference = x.quiz.PublicId,
                QuizTitle = x.quiz.Title, AttemptNo = x.attempt.AttemptNo,
                State = x.attempt.State, SubmittedAt = x.attempt.SubmittedAt,
                ObtainedMarks = x.attempt.ObtainedMarks, PassMarks = x.quiz.PassMarks,
                Passed = x.attempt.ObtainedMarks.HasValue
                    ? x.attempt.ObtainedMarks.Value >= x.quiz.PassMarks : null,
                IsArchived = !course.IsActive
            }).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<QuizHistoryRowDto>>.SuccessResponse(new PagedResult<QuizHistoryRowDto>
        { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }
}
