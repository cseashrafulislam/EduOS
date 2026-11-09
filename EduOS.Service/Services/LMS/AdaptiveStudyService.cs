using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;

namespace EduOS.Service.Services.LMS;

/// <summary>Teacher-approved adaptive targets over canonical graded LMS attempts.
/// No independent mark ledger, provider call, or automatic transcript changes.</summary>
public sealed class AdaptiveStudyService(EduOSDbContext db, ICurrentUserService user,
    TimeProvider clock, ILogger<AdaptiveStudyService> logger)
{
    private bool Admin => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal"));
    private bool Teacher => user.IsAuthenticated && user.TenantId > 0 && user.IsInRole("Teacher");
    private bool Student => user.IsAuthenticated && user.TenantId > 0 && user.IsInRole("Student");
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<long> OwnStudentId(CancellationToken ct) => Student
        ? await db.Students.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.UserId == user.UserId && x.StatusCode == "Active" && !x.IsDeleted)
            .Select(x => x.Id).FirstOrDefaultAsync(ct) : 0;

    private async Task<bool> CanAuthor(Course course, CancellationToken ct)
    {
        if (course.IsTeacherCpd || !course.IsActive || course.IsDeleted) return false;
        if (Admin) return true;
        if (!Teacher || !course.PrimaryInstructorEmployeeId.HasValue) return false;
        return await db.Employees.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.Id == course.PrimaryInstructorEmployeeId && x.UserId == user.UserId &&
            x.CanTeach && x.State == EmployeeState.Active && !x.IsDeleted, ct);
    }

    private async Task<Course?> Course(Guid reference, CancellationToken ct) =>
        reference == Guid.Empty ? null : await db.Courses.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == reference && !x.IsTeacherCpd &&
            !x.IsDeleted, ct);

    public async Task<ApiResponse<PagedResult<AdaptiveCourseDto>>> CoursesAsync(
        bool author, int page, int pageSize, CancellationToken ct = default)
    {
        if ((author && !Admin && !Teacher) || (!author && !Student))
            return Fail<PagedResult<AdaptiveCourseDto>>("Role not permitted.", 403);
        if (page < 1 || pageSize is < 1 or > 30 || (long)(page - 1) * pageSize > int.MaxValue)
            return Fail<PagedResult<AdaptiveCourseDto>>("Invalid paging.");
        var courses = db.Courses.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.IsActive && !x.IsTeacherCpd && !x.IsDeleted);
        if (author && !Admin)
        {
            var employeeId = await db.Employees.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.UserId == user.UserId && x.CanTeach && x.State == EmployeeState.Active && !x.IsDeleted)
                .Select(x => x.Id).FirstOrDefaultAsync(ct);
            courses = courses.Where(x => employeeId > 0 && x.PrimaryInstructorEmployeeId == employeeId);
        }
        if (!author)
        {
            var studentId = await OwnStudentId(ct);
            courses = courses.Where(x => studentId > 0 && db.CourseEnrollments.Any(e =>
                e.TenantId == user.TenantId && e.CourseId == x.Id && e.StudentId == studentId &&
                !e.IsDeleted && (e.State == CourseEnrollmentState.Active ||
                                  e.State == CourseEnrollmentState.Completed)));
        }
        var count = await courses.CountAsync(ct);
        var rows = await courses.OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdaptiveCourseDto { Reference = x.PublicId, Title = x.Title })
            .ToListAsync(ct);
        return ApiResponse<PagedResult<AdaptiveCourseDto>>.SuccessResponse(new PagedResult<AdaptiveCourseDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<IReadOnlyList<AdaptiveChapterDto>>> ChaptersAsync(
        Guid courseReference, bool author, CancellationToken ct = default)
    {
        var course = await Course(courseReference, ct);
        if (course == null) return Fail<IReadOnlyList<AdaptiveChapterDto>>("Course not found.", 404);
        var studentId = author ? 0 : await OwnStudentId(ct);
        var allowed = author ? await CanAuthor(course, ct) :
            studentId > 0 && await HasEnrollment(course.Id, studentId, ct);
        if (!allowed)
            return Fail<IReadOnlyList<AdaptiveChapterDto>>("Course access denied.", 403);
        var sections = await db.CourseSections.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseId == course.Id && x.IsPublished && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Take(100)
            .Select(x => new AdaptiveChapterDto { Id = x.Id, Title = x.Title }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AdaptiveChapterDto>>.SuccessResponse(sections);
    }

    public async Task<ApiResponse<PagedResult<AdaptiveRosterDto>>> RosterAsync(
        Guid courseReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return Fail<PagedResult<AdaptiveRosterDto>>("Invalid paging.");
        var course = await Course(courseReference, ct);
        if (course == null || !await CanAuthor(course, ct))
            return Fail<PagedResult<AdaptiveRosterDto>>("Course access denied.", 403);
        var query = from enrollment in db.CourseEnrollments.AsNoTracking()
            join student in db.Students.AsNoTracking() on enrollment.StudentId equals (long?)student.Id
            where enrollment.TenantId == user.TenantId && student.TenantId == user.TenantId &&
                enrollment.CourseId == course.Id && !enrollment.IsDeleted && !student.IsDeleted &&
                (enrollment.State == CourseEnrollmentState.Active ||
                 enrollment.State == CourseEnrollmentState.Completed)
            select student;
        var count = await query.Select(x => x.Id).Distinct().CountAsync(ct);
        var rows = await query.Distinct().OrderBy(x => x.StudentCode).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdaptiveRosterDto
            { StudentReference = x.PublicId, StudentCode = x.StudentCode, FullName = x.FullName })
            .ToListAsync(ct);
        return ApiResponse<PagedResult<AdaptiveRosterDto>>.SuccessResponse(new PagedResult<AdaptiveRosterDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<IReadOnlyList<AdaptiveMappedSkillDto>>> SkillsAsync(
        Guid courseReference, CancellationToken ct = default)
    {
        var course = await Course(courseReference, ct);
        if (course == null || !await CanAuthor(course, ct))
            return Fail<IReadOnlyList<AdaptiveMappedSkillDto>>("Course access denied.", 403);
        var mapped = await (from quiz in db.Quizzes.AsNoTracking()
            join section in db.CourseSections.AsNoTracking() on quiz.CourseSectionId equals (long?)section.Id
            where quiz.TenantId == user.TenantId && section.TenantId == user.TenantId &&
                quiz.CourseId == course.Id && section.CourseId == course.Id &&
                quiz.IsPublished && section.IsPublished && !quiz.IsDeleted && !section.IsDeleted &&
                quiz.SkillCode != null
            select new { SectionId = section.Id, section.Title, quiz.SkillCode })
            .Distinct().OrderBy(x => x.Title).ThenBy(x => x.SkillCode).Take(100).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AdaptiveMappedSkillDto>>.SuccessResponse(mapped.Select(x =>
            new AdaptiveMappedSkillDto { CourseSectionId = x.SectionId,
                Chapter = x.Title, SkillCode = x.SkillCode! }).ToArray());
    }

    public async Task<ApiResponse<IReadOnlyList<AdaptiveGoalDto>>> GoalsAsync(
        Guid courseReference, Guid studentReference, CancellationToken ct = default)
    {
        var course = await Course(courseReference, ct);
        if (course == null || !await CanAuthor(course, ct))
            return Fail<IReadOnlyList<AdaptiveGoalDto>>("Course access denied.", 403);
        var studentId = await db.Students.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.PublicId == studentReference && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (studentId == 0 || !await HasEnrollment(course.Id, studentId, ct))
            return Fail<IReadOnlyList<AdaptiveGoalDto>>("Student enrollment not found.", 404);
        var rows = await db.AdaptiveStudyGoals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseId == course.Id && x.StudentId == studentId && !x.IsDeleted)
            .OrderBy(x => x.CourseSectionId).ThenBy(x => x.SkillCode).Take(100).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AdaptiveGoalDto>>.SuccessResponse(rows.Select(x => new AdaptiveGoalDto
        { CourseSectionId = x.CourseSectionId, SkillCode = x.SkillCode, TargetPercent = x.TargetPercent,
            TeacherNote = x.TeacherNote, RowVersion = Convert.ToBase64String(x.RowVersion) }).ToArray());
    }

    private Task<bool> HasEnrollment(long courseId, long studentId, CancellationToken ct) =>
        db.CourseEnrollments.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.CourseId == courseId && x.StudentId == studentId && !x.IsDeleted &&
            (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed), ct);

    public async Task<ApiResponse<AdaptiveGoalDto>> SaveGoalAsync(
        SaveAdaptiveGoalDto request, CancellationToken ct = default)
    {
        if (request == null || request.CourseReference == Guid.Empty ||
            request.StudentReference == Guid.Empty || request.CourseSectionId <= 0 ||
            !ValidSkill(request.SkillCode) || request.TargetPercent is < 0 or > 100 ||
            request.TeacherNote?.Length > 500)
            return Fail<AdaptiveGoalDto>("Invalid teacher goal.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var course = await Course(request.CourseReference, token);
                if (course == null || !await CanAuthor(course, token))
                    return Fail<AdaptiveGoalDto>("Course access denied.", 403);
                var studentId = await db.Students.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                    x.PublicId == request.StudentReference && !x.IsDeleted)
                    .Select(x => x.Id).FirstOrDefaultAsync(token);
                var skill = request.SkillCode.Trim().ToUpperInvariant();
                if (studentId == 0 || !await HasEnrollment(course.Id, studentId, token) ||
                    !await db.CourseSections.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                        x.Id == request.CourseSectionId && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted, token) ||
                    !await db.Quizzes.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                        x.CourseId == course.Id && x.CourseSectionId == request.CourseSectionId &&
                        x.SkillCode == skill && x.IsPublished && !x.IsDeleted, token))
                    return Fail<AdaptiveGoalDto>("Enrollment or mapped published skill not found.", 404);
                var goal = await db.AdaptiveStudyGoals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    x.CourseId == course.Id && x.StudentId == studentId &&
                    x.CourseSectionId == request.CourseSectionId && x.SkillCode == skill && !x.IsDeleted, token);
                if (goal != null && !Matches(goal.RowVersion, request.RowVersion))
                    return Fail<AdaptiveGoalDto>("Goal changed; reload before editing.", 409);
                var now = Now;
                if (goal == null)
                {
                    goal = new AdaptiveStudyGoal { TenantId = user.TenantId, CourseId = course.Id,
                        StudentId = studentId, CourseSectionId = request.CourseSectionId, SkillCode = skill,
                        CreatedAt = now, CreatedBy = user.UserId };
                    db.AdaptiveStudyGoals.Add(goal);
                }
                else { goal.UpdatedAt = now; goal.UpdatedBy = user.UserId; }
                goal.TargetPercent = request.TargetPercent;
                goal.TeacherNote = request.TeacherNote?.Trim();
                goal.SetByUserId = user.UserId;
                await db.SaveChangesAsync(token);
                return ApiResponse<AdaptiveGoalDto>.SuccessResponse(new AdaptiveGoalDto
                { CourseSectionId = goal.CourseSectionId, SkillCode = goal.SkillCode,
                    TargetPercent = goal.TargetPercent, TeacherNote = goal.TeacherNote,
                    RowVersion = Convert.ToBase64String(goal.RowVersion) });
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<AdaptiveGoalDto>("Goal changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Adaptive goal write conflict tenant {TenantId}", user.TenantId);
            return Fail<AdaptiveGoalDto>("Goal write conflict. Reload.", 409);
        }
    }

    private static bool ValidSkill(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private static bool Matches(byte[] actual, string? submitted)
    {
        if (string.IsNullOrWhiteSpace(submitted)) return false;
        try
        {
            var value = Convert.FromBase64String(submitted);
            return actual.Length > 0 && value.Length == actual.Length &&
                CryptographicOperations.FixedTimeEquals(actual, value);
        }
        catch (FormatException) { return false; }
    }

    private async Task<(Course? Course, CourseEnrollment? Enrollment)> OwnCourse(
        Guid reference, CancellationToken ct, bool requireActive = true)
    {
        var course = await Course(reference, ct);
        if (course == null || (requireActive && !course.IsActive)) return (null, null);
        var studentId = await OwnStudentId(ct);
        if (studentId == 0) return (null, null);
        var enrollment = await db.CourseEnrollments.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.StudentId == studentId && x.CourseId == course.Id && !x.IsDeleted &&
            (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed))
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        return enrollment == null ? (null, null) : (course, enrollment);
    }

    public async Task<ApiResponse<AdaptivePlanDto>> CurrentAsync(Guid courseReference, CancellationToken ct = default)
    {
        var (course, enrollment) = await OwnCourse(courseReference, ct);
        if (course == null || enrollment == null)
            return Fail<AdaptivePlanDto>("Active authorized enrollment required.", 403);
        return await BuildPlan(course, enrollment, ct);
    }

    private async Task<ApiResponse<AdaptivePlanDto>> BuildPlan(
        Course course, CourseEnrollment enrollment, CancellationToken ct)
    {
        var sections = await db.CourseSections.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseId == course.Id && x.IsPublished && !x.IsDeleted)
            .Select(x => new { x.Id, x.Title }).Take(101).ToListAsync(ct);
        if (sections.Count > 100) return Fail<AdaptivePlanDto>("Too many chapters to build one plan.", 409);
        var sectionIds = sections.Select(x => x.Id).ToArray();
        var quizzes = await db.Quizzes.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseId == course.Id && x.IsPublished && !x.IsDeleted && x.CourseSectionId.HasValue &&
            x.SkillCode != null && sectionIds.Contains(x.CourseSectionId.Value))
            .OrderBy(x => x.Id).Take(201).Select(x => new { x.Id, x.PublicId, x.CourseSectionId,
                x.SkillCode, x.MaxAttempts, x.OpensAt, x.ClosesAt, x.PracticeDifficulty }).ToListAsync(ct);
        if (quizzes.Count > 200) return Fail<AdaptivePlanDto>("Too many mapped quizzes to build one plan.", 409);
        var quizIds = quizzes.Select(x => x.Id).ToArray();
        var totals = await db.QuizQuestions.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            quizIds.Contains(x.QuizId) && !x.IsDeleted)
            .GroupBy(x => x.QuizId).Select(g => new { QuizId = g.Key, Total = g.Sum(x => x.Marks) })
            .ToDictionaryAsync(x => x.QuizId, x => x.Total, ct);
        var attemptQuery = db.QuizAttempts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseEnrollmentId == enrollment.Id && quizIds.Contains(x.QuizId) && !x.IsDeleted);
        var usage = await attemptQuery.GroupBy(x => x.QuizId)
            .Select(g => new { QuizId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.QuizId, x => x.Count, ct);
        var lastGraded = await attemptQuery.Where(x => x.State == QuizAttemptState.Graded &&
                x.SubmittedAt.HasValue && x.ObtainedMarks.HasValue)
            .GroupBy(x => x.QuizId)
            .Select(g => new { QuizId = g.Key,
                Marks = g.OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id)
                    .Select(x => x.ObtainedMarks).FirstOrDefault() })
            .ToDictionaryAsync(x => x.QuizId, x => x.Marks, ct);
        var goals = await db.AdaptiveStudyGoals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseId == course.Id && x.StudentId == enrollment.StudentId && !x.IsDeleted)
            .Select(x => new { x.CourseSectionId, x.SkillCode, x.TargetPercent, x.TeacherNote }).ToListAsync(ct);
        var now = Now;
        var skills = quizzes.GroupBy(q => new { q.CourseSectionId, q.SkillCode }).Select(group =>
        {
            var first = group.First();
            var goal = goals.FirstOrDefault(g => g.CourseSectionId == first.CourseSectionId &&
                g.SkillCode == first.SkillCode);
            var target = goal?.TargetPercent ?? 80m;
            decimal earned = 0, possible = 0;
            var gradedCount = 0;
            var remaining = 0;
            Guid? suggested = null;
            foreach (var quiz in group.OrderBy(x => x.PracticeDifficulty).ThenBy(x => x.Id))
            {
                if (lastGraded.TryGetValue(quiz.Id, out var latestMarks) && latestMarks.HasValue &&
                    totals.TryGetValue(quiz.Id, out var full) && full > 0 &&
                    latestMarks.Value >= 0 && latestMarks.Value <= full)
                { earned += latestMarks.Value; possible += full; gradedCount++; }
                var available = Math.Max(0, quiz.MaxAttempts - usage.GetValueOrDefault(quiz.Id));
                if (quiz.OpensAt > now || quiz.ClosesAt < now) available = 0;
                remaining += available;
                if (available > 0 && suggested == null) suggested = quiz.PublicId;
            }
            var observed = possible > 0 ? Math.Round(earned * 100m / possible, 2,
                MidpointRounding.AwayFromZero) : (decimal?)null;
            return new AdaptiveSkillDto
            {
                CourseSectionId = first.CourseSectionId!.Value,
                Chapter = sections.First(s => s.Id == first.CourseSectionId).Title,
                SkillCode = first.SkillCode!, TargetPercent = target, ObservedPercent = observed,
                Priority = observed == null ? "NoEvidence" : observed < 50m ? "Review" :
                    observed < target ? "Practice" : "Extend",
                GradedAttempts = gradedCount, PracticeAttemptsRemaining = remaining,
                RecommendedQuizReference = suggested, TeacherNote = goal?.TeacherNote
            };
        }).OrderBy(x => x.ObservedPercent.HasValue).ThenBy(x => x.ObservedPercent)
            .ThenBy(x => x.Chapter).ThenBy(x => x.SkillCode).Take(100).ToArray();
        return ApiResponse<AdaptivePlanDto>.SuccessResponse(new AdaptivePlanDto
        { CourseReference = course.PublicId, CourseTitle = course.Title, GeneratedAt = now, Skills = skills });
    }

    public async Task<ApiResponse<AdaptivePlanDto>> SavePlanAsync(
        SaveAdaptivePlanDto request, CancellationToken ct = default)
    {
        if (request == null || request.CourseReference == Guid.Empty || request.ClientRequestId == Guid.Empty)
            return Fail<AdaptivePlanDto>("Course and request key required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var (course, enrollment) = await OwnCourse(request.CourseReference, token);
                if (course == null || enrollment == null)
                    return Fail<AdaptivePlanDto>("Active authorized enrollment required.", 403);
                var replay = await db.AdaptiveStudyPlanHistories.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, token);
                if (replay != null)
                    return replay.CourseEnrollmentId != enrollment.Id
                        ? Fail<AdaptivePlanDto>("Request key is already used for another enrollment.", 409)
                        : ApiResponse<AdaptivePlanDto>.SuccessResponse(ReadSnapshot(replay));
                var calculation = await BuildPlan(course, enrollment, token);
                if (!calculation.Success || calculation.Data == null) return calculation;
                var data = calculation.Data;
                var row = new AdaptiveStudyPlanHistory
                { TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                    CourseEnrollmentId = enrollment.Id, StudentId = enrollment.StudentId!.Value,
                    AlgorithmVersion = 1, GeneratedAt = data.GeneratedAt, CreatedAt = data.GeneratedAt,
                    CreatedBy = user.UserId, PlanJson = JsonSerializer.Serialize(data) };
                db.AdaptiveStudyPlanHistories.Add(row);
                await db.SaveChangesAsync(token);
                data.HistoryId = row.Id;
                return ApiResponse<AdaptivePlanDto>.SuccessResponse(data);
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Adaptive plan write conflict tenant {TenantId}", user.TenantId);
            return Fail<AdaptivePlanDto>("Plan save conflict. Retry with the same request key.", 409);
        }
    }

    private static AdaptivePlanDto ReadSnapshot(AdaptiveStudyPlanHistory row)
    {
        var plan = JsonSerializer.Deserialize<AdaptivePlanDto>(row.PlanJson) ?? new AdaptivePlanDto();
        plan.HistoryId = row.Id;
        return plan;
    }

    public async Task<ApiResponse<PagedResult<AdaptivePlanHistoryRowDto>>> HistoryAsync(
        Guid courseReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 30 || (long)(page - 1) * pageSize > int.MaxValue)
            return Fail<PagedResult<AdaptivePlanHistoryRowDto>>("Invalid paging.");
        var (course, enrollment) = await OwnCourse(courseReference, ct, requireActive: false);
        if (course == null || enrollment == null)
            return Fail<PagedResult<AdaptivePlanHistoryRowDto>>("Course access denied.", 403);
        var query = db.AdaptiveStudyPlanHistories.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseEnrollmentId == enrollment.Id && x.StudentId == enrollment.StudentId && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.GeneratedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.GeneratedAt, x.PlanJson }).ToListAsync(ct);
        return ApiResponse<PagedResult<AdaptivePlanHistoryRowDto>>.SuccessResponse(new PagedResult<AdaptivePlanHistoryRowDto>
        { Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => new AdaptivePlanHistoryRowDto
            { Id = x.Id, GeneratedAt = x.GeneratedAt,
                SkillCount = JsonSerializer.Deserialize<AdaptivePlanDto>(x.PlanJson)?.Skills?.Count ?? 0 }).ToList() });
    }

    public async Task<ApiResponse<AdaptivePlanDto>> HistoricalAsync(long id, CancellationToken ct = default)
    {
        var studentId = await OwnStudentId(ct);
        if (studentId == 0 || id <= 0) return Fail<AdaptivePlanDto>("Plan not found.", 404);
        var row = await db.AdaptiveStudyPlanHistories.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.StudentId == studentId && x.Id == id && !x.IsDeleted, ct);
        return row == null ? Fail<AdaptivePlanDto>("Plan not found.", 404)
            : ApiResponse<AdaptivePlanDto>.SuccessResponse(ReadSnapshot(row));
    }
}
