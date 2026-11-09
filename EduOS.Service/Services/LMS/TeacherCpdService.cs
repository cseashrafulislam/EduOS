using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.LMS;

/// <summary>Employee participation in the canonical LMS course/enrollment/lesson evidence.
/// No shadow teacher enrollment, student identity or assessment ledger is introduced.</summary>
public sealed class TeacherCpdService(EduOSDbContext db, ICurrentUserService user,
    TimeProvider clock, ILogger<TeacherCpdService> logger)
{
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);

    private bool Allowed => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        user.IsInRole("Teacher");
    private bool Manager => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal"));

    public async Task<ApiResponse<PagedResult<TeacherCpdCourseDto>>> ManagementCoursesAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Manager) return Fail<PagedResult<TeacherCpdCourseDto>>("Institution administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 25 || search?.Length > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Fail<PagedResult<TeacherCpdCourseDto>>("Invalid course search.");
        var query = db.Courses.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.IsTeacherCpd && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.Code.StartsWith(term));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(x => new TeacherCpdCourseDto
            { Reference = x.PublicId, Title = x.Title, Code = x.Code, Description = x.Description })
            .ToListAsync(ct);
        return ApiResponse<PagedResult<TeacherCpdCourseDto>>.SuccessResponse(new PagedResult<TeacherCpdCourseDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<PagedResult<TeacherCpdParticipantDto>>> ParticipantsAsync(
        Guid courseReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Manager) return Fail<PagedResult<TeacherCpdParticipantDto>>("Institution administrator required.", 403);
        if (courseReference == Guid.Empty || page < 1 || pageSize is < 1 or > 25 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Fail<PagedResult<TeacherCpdParticipantDto>>("Invalid CPD register request.");
        var courseId = await db.Courses.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.PublicId == courseReference &&
            x.IsTeacherCpd && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (courseId == 0) return Fail<PagedResult<TeacherCpdParticipantDto>>("CPD course not found.", 404);
        var query = from enrollment in db.CourseEnrollments.AsNoTracking()
            join employee in db.Employees.AsNoTracking() on enrollment.EmployeeId equals (long?)employee.Id
            where enrollment.TenantId == user.TenantId && employee.TenantId == user.TenantId &&
                enrollment.CourseId == courseId && !enrollment.IsDeleted && !employee.IsDeleted
            select new { enrollment, employee };
        var count = await query.CountAsync(ct);
        var selected = await query.OrderBy(x => x.employee.EmployeeCode).ThenBy(x => x.enrollment.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(x => new
            {
                EnrollmentId = x.enrollment.Id, EmployeeReference = x.employee.PublicId,
                EmployeeName = x.employee.FullName, EmployeeCode = x.employee.EmployeeCode,
                x.enrollment.State, x.enrollment.EnrolledAt, x.enrollment.CompletedAt
            }).ToListAsync(ct);
        var rows = selected.Select(x => new TeacherCpdParticipantDto
        {
            EnrollmentId = x.EnrollmentId, EmployeeReference = x.EmployeeReference,
            EmployeeName = x.EmployeeName, EmployeeCode = x.EmployeeCode,
            State = x.State.ToString(), EnrolledAt = x.EnrolledAt, CompletedAt = x.CompletedAt
        }).ToList();
        var ids = rows.Select(x => x.EnrollmentId).ToArray();
        var issued = await db.LmsCompletionCertificates.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && ids.Contains(x.CourseEnrollmentId) &&
            x.EmployeeId != null && !x.IsDeleted && !x.IsRevoked)
            .Select(x => new { x.CourseEnrollmentId, x.PublicId }).ToListAsync(ct);
        foreach (var row in rows)
            row.CertificateReference = issued.FirstOrDefault(x => x.CourseEnrollmentId == row.EnrollmentId)?.PublicId;
        return ApiResponse<PagedResult<TeacherCpdParticipantDto>>.SuccessResponse(new PagedResult<TeacherCpdParticipantDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    private async Task<long> TeacherIdAsync(CancellationToken ct) =>
        await db.Employees.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.UserId == user.UserId && x.CanTeach && x.State == EmployeeState.Active &&
            !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);

    private async Task<(Course? Course, CourseEnrollment? Enrollment)> OwnedAsync(
        long enrollmentId, long employeeId, CancellationToken ct)
    {
        var enrollment = await db.CourseEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == enrollmentId && x.EmployeeId == employeeId &&
            x.StudentId == null && !x.IsDeleted, ct);
        if (enrollment == null) return (null, null);
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == enrollment.CourseId &&
            x.IsTeacherCpd && !x.IsDeleted, ct);
        return (course, enrollment);
    }

    public async Task<ApiResponse<PagedResult<TeacherCpdCourseDto>>> CoursesAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed || await TeacherIdAsync(ct) == 0)
            return Fail<PagedResult<TeacherCpdCourseDto>>("Active teacher permission required.", 403);
        if (page < 1 || pageSize is < 1 or > 25 || search?.Length > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Fail<PagedResult<TeacherCpdCourseDto>>("Invalid search or page.");
        var teacherId = await TeacherIdAsync(ct);
        var query = db.Courses.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.IsTeacherCpd && x.IsActive && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.Code.StartsWith(term));
        }
        var count = await query.CountAsync(ct);
        var courses = await query.OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.PublicId, x.Title, x.Code, x.Description }).ToListAsync(ct);
        var ids = courses.Select(x => x.Id).ToArray();
        var enrollments = await db.CourseEnrollments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.EmployeeId == teacherId &&
            ids.Contains(x.CourseId) && !x.IsDeleted).ToListAsync(ct);
        var enrollmentIds = enrollments.Select(x => x.Id).ToArray();
        var lessons = await db.Lessons.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && ids.Contains(x.CourseId) && x.IsPublished && !x.IsDeleted)
            .Select(x => new { x.Id, x.CourseId }).ToListAsync(ct);
        var lessonIds = lessons.Select(x => x.Id).ToArray();
        var progress = await db.LessonProgresses.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && enrollmentIds.Contains(x.CourseEnrollmentId) &&
            lessonIds.Contains(x.LessonId) && x.IsCompleted && !x.IsDeleted)
            .Select(x => new { x.CourseEnrollmentId, x.LessonId }).ToListAsync(ct);
        var certificates = await db.LmsCompletionCertificates.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && enrollmentIds.Contains(x.CourseEnrollmentId) &&
            x.EmployeeId == teacherId && !x.IsRevoked && !x.IsDeleted)
            .Select(x => new { x.CourseEnrollmentId, x.PublicId }).ToListAsync(ct);
        var rows = courses.Select(x =>
        {
            var enrollment = enrollments.FirstOrDefault(e => e.CourseId == x.Id);
            var lessonIdsForCourse = lessons.Where(l => l.CourseId == x.Id).Select(l => l.Id).ToHashSet();
            return new TeacherCpdCourseDto
            {
                Reference = x.PublicId, Title = x.Title, Code = x.Code, Description = x.Description,
                EnrollmentId = enrollment?.Id, State = enrollment?.State.ToString() ?? "NotEnrolled",
                RowVersion = enrollment == null ? null : Convert.ToBase64String(enrollment.RowVersion),
                CompletedAt = enrollment?.CompletedAt,
                CertificateReference = certificates.FirstOrDefault(c => c.CourseEnrollmentId == enrollment?.Id)?.PublicId,
                TotalLessons = lessonIdsForCourse.Count,
                CompletedLessons = enrollment == null ? 0 : progress.Count(p =>
                    p.CourseEnrollmentId == enrollment.Id && lessonIdsForCourse.Contains(p.LessonId))
            };
        }).ToList();
        return ApiResponse<PagedResult<TeacherCpdCourseDto>>.SuccessResponse(new PagedResult<TeacherCpdCourseDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<TeacherCpdCourseDto>> EnrollAsync(
        TeacherCpdEnrollRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<TeacherCpdCourseDto>("Teacher permission required.", 403);
        if (request == null || request.CourseReference == Guid.Empty || request.ClientRequestId == Guid.Empty)
            return Fail<TeacherCpdCourseDto>("Course and request reference required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var teacherId = await TeacherIdAsync(token);
                if (teacherId == 0) return Fail<TeacherCpdCourseDto>("Active teacher required.", 403);
                var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == request.CourseReference &&
                    x.IsTeacherCpd && x.IsActive && !x.IsDeleted, token);
                if (course == null) return Fail<TeacherCpdCourseDto>("Active CPD course not found.", 404);
                var replay = await db.CourseEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (replay != null && (replay.CourseId != course.Id || replay.EmployeeId != teacherId))
                    return Fail<TeacherCpdCourseDto>("Request reference belongs to a different participant.", 409);
                var existing = await db.CourseEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.CourseId == course.Id &&
                    x.EmployeeId == teacherId && !x.IsDeleted, token);
                var row = replay ?? existing;
                if (row == null)
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    row = new CourseEnrollment
                    {
                        TenantId = user.TenantId, CourseId = course.Id,
                        ClientRequestId = request.ClientRequestId, EmployeeId = teacherId,
                        State = CourseEnrollmentState.Active, EnrolledAt = now,
                        CreatedAt = now, CreatedBy = user.UserId
                    };
                    db.CourseEnrollments.Add(row);
                    await db.SaveChangesAsync(token);
                    db.AuditLogs.Add(new AuditLog
                    {
                        TenantId = user.TenantId, UserId = user.UserId, EntityName = nameof(CourseEnrollment),
                        EntityId = row.Id, Action = "LMS.TeacherCpdEnrolled",
                        OccurredAt = now, IsSuccess = true
                    });
                    await db.SaveChangesAsync(token);
                }
                return ApiResponse<TeacherCpdCourseDto>.SuccessResponse(new TeacherCpdCourseDto
                {
                    Reference = course.PublicId, Title = course.Title, Code = course.Code,
                    Description = course.Description, EnrollmentId = row.Id,
                    State = row.State.ToString(), RowVersion = Convert.ToBase64String(row.RowVersion),
                    CompletedAt = row.CompletedAt
                }, existing == null && replay == null ? "CPD enrollment created." : "CPD enrollment already exists.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "CPD enrollment race tenant {TenantId}", user.TenantId);
            return Fail<TeacherCpdCourseDto>("CPD enrollment changed. Retry.", 409);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<TeacherCpdLessonDto>>> LessonsAsync(
        long enrollmentId, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<IReadOnlyList<TeacherCpdLessonDto>>("Teacher permission required.", 403);
        var teacherId = await TeacherIdAsync(ct);
        var (course, enrollment) = await OwnedAsync(enrollmentId, teacherId, ct);
        if (course == null || enrollment == null)
            return Fail<IReadOnlyList<TeacherCpdLessonDto>>("CPD enrollment not found.", 404);
        if (await db.Lessons.AsNoTracking().CountAsync(x =>
            x.TenantId == user.TenantId && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted, ct) > 500)
            return Fail<IReadOnlyList<TeacherCpdLessonDto>>(
                "Course has too many published lessons for one page; contact the course administrator.", 409);
        var lessons = await db.Lessons.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.PublicId, x.Title, x.Content, x.ContentUrl }).Take(500).ToListAsync(ct);
        var lessonIds = lessons.Select(x => x.Id).ToArray();
        var progress = await db.LessonProgresses.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.CourseEnrollmentId == enrollmentId &&
            lessonIds.Contains(x.LessonId) && !x.IsDeleted).ToDictionaryAsync(x => x.LessonId, ct);
        return ApiResponse<IReadOnlyList<TeacherCpdLessonDto>>.SuccessResponse(lessons.Select(x =>
        {
            progress.TryGetValue(x.Id, out var item);
            return new TeacherCpdLessonDto
            {
                Reference = x.PublicId, Title = x.Title, Content = x.Content, ContentUrl = x.ContentUrl,
                IsCompleted = item?.IsCompleted ?? false, ProgressPercent = item?.ProgressPercent ?? 0
            };
        }).ToList());
    }

    public async Task<ApiResponse<TeacherCpdLessonDto>> SaveLessonAsync(
        long enrollmentId, TeacherCpdLessonRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<TeacherCpdLessonDto>("Teacher permission required.", 403);
        if (enrollmentId <= 0 || request == null || request.LessonReference == Guid.Empty ||
            request.ProgressPercent is < 0 or > 100)
            return Fail<TeacherCpdLessonDto>("Invalid CPD lesson progress.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var teacherId = await TeacherIdAsync(token);
                var (course, enrollment) = await OwnedAsync(enrollmentId, teacherId, token);
                if (course == null || !course.IsActive || enrollment?.State != CourseEnrollmentState.Active)
                    return Fail<TeacherCpdLessonDto>("Active CPD enrollment required.", 409);
                var lesson = await db.Lessons.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.CourseId == course.Id &&
                    x.PublicId == request.LessonReference && x.IsPublished && !x.IsDeleted, token);
                if (lesson == null) return Fail<TeacherCpdLessonDto>("Published CPD lesson not found.", 404);
                var now = clock.GetUtcNow().UtcDateTime;
                var progress = await db.LessonProgresses.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.CourseEnrollmentId == enrollment.Id &&
                    x.LessonId == lesson.Id && !x.IsDeleted, token);
                if (progress == null)
                {
                    progress = new LessonProgress
                    {
                        TenantId = user.TenantId, CourseEnrollmentId = enrollment.Id,
                        LessonId = lesson.Id, CreatedAt = now, CreatedBy = user.UserId
                    };
                    db.LessonProgresses.Add(progress);
                }
                else { progress.UpdatedAt = now; progress.UpdatedBy = user.UserId; }
                var newlyCompleted = request.IsCompleted && !progress.IsCompleted;
                progress.IsCompleted |= request.IsCompleted;
                progress.ProgressPercent = progress.IsCompleted ? 100m : Math.Max(progress.ProgressPercent, request.ProgressPercent);
                progress.CompletedAt = progress.IsCompleted ? progress.CompletedAt ?? now : null;
                progress.LastAccessedAt = now;
                await db.SaveChangesAsync(token);
                if (newlyCompleted)
                {
                    db.AuditLogs.Add(new AuditLog
                    {
                        TenantId = user.TenantId, UserId = user.UserId, EntityName = nameof(LessonProgress),
                        EntityId = progress.Id, Action = "LMS.TeacherCpdLessonCompleted",
                        OccurredAt = now, IsSuccess = true
                    });
                    await db.SaveChangesAsync(token);
                }
                return ApiResponse<TeacherCpdLessonDto>.SuccessResponse(new TeacherCpdLessonDto
                {
                    Reference = lesson.PublicId, Title = lesson.Title, Content = lesson.Content,
                    ContentUrl = lesson.ContentUrl, IsCompleted = progress.IsCompleted,
                    ProgressPercent = progress.ProgressPercent
                });
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "CPD progress race tenant {TenantId}", user.TenantId);
            return Fail<TeacherCpdLessonDto>("Lesson progress changed. Retry.", 409);
        }
    }
}
