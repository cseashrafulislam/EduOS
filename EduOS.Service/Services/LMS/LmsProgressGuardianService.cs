using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.LMS;

/// <summary>Fail-closed, per-enrollment progress disclosure. Existing certificate grants
/// and guardian contact preferences never authorize this service.</summary>
public sealed class LmsProgressGuardianService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<LmsProgressGuardianService> logger) : ILmsProgressGuardianService
{
    private bool Student => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0 && user.IsInRole("Student");
    private bool Guardian => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0 &&
        (user.IsInRole("Guardian") || user.IsInRole("Parent"));
    private static ApiResponse<T> Deny<T>() => ApiResponse<T>.ErrorResponse("Progress not found.", 404);
    private static ApiResponse<T> Invalid<T>() => ApiResponse<T>.ErrorResponse("Invalid progress sharing request.", 400);
    private static bool SqlConflict(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql &&
                sql.Number is 1205 or 1222 or 3960 or 2601 or 2627) return true;
        return false;
    }

    private IQueryable<CourseEnrollment> Owned() =>
        from enrollment in db.CourseEnrollments
        join learner in db.Students on enrollment.StudentId equals learner.Id
        join course in db.Courses on enrollment.CourseId equals course.Id
        where enrollment.TenantId == user.TenantId && learner.TenantId == user.TenantId &&
            course.TenantId == user.TenantId && learner.UserId == user.UserId &&
            !enrollment.IsDeleted && !learner.IsDeleted && !course.IsDeleted && course.IsActive &&
            (enrollment.State == CourseEnrollmentState.Active ||
             enrollment.State == CourseEnrollmentState.Completed)
        select enrollment;

    private IQueryable<LmsGuardianChoiceDto> EligibleLinks(long studentId) =>
        from link in db.StudentGuardians.AsNoTracking()
        join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
        where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
            link.StudentId == studentId && !link.IsDeleted && !guardian.IsDeleted &&
            guardian.IsActive && guardian.UserId.HasValue && guardian.UserId.Value > 0
        select new LmsGuardianChoiceDto
        {
            GuardianLinkId = link.Id, GuardianName = guardian.FullName, RelationCode = link.RelationCode
        };

    public async Task<ApiResponse<IReadOnlyList<LmsGuardianChoiceDto>>> EligibleAsync(long enrollmentId, CancellationToken ct = default)
    {
        if (!Student || enrollmentId <= 0) return Deny<IReadOnlyList<LmsGuardianChoiceDto>>();
        var studentId = await Owned().AsNoTracking().Where(x => x.Id == enrollmentId)
            .Select(x => x.StudentId).FirstOrDefaultAsync(ct);
        if (studentId == 0) return Deny<IReadOnlyList<LmsGuardianChoiceDto>>();
        var rows = await EligibleLinks(studentId).OrderBy(x => x.GuardianName)
            .ThenBy(x => x.GuardianLinkId).Take(25).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LmsGuardianChoiceDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<LmsProgressGuardianGrantDto>>> GrantsAsync(long enrollmentId, CancellationToken ct = default)
    {
        if (!Student || enrollmentId <= 0) return Deny<IReadOnlyList<LmsProgressGuardianGrantDto>>();
        var studentId = await Owned().AsNoTracking().Where(x => x.Id == enrollmentId)
            .Select(x => x.StudentId).FirstOrDefaultAsync(ct);
        if (studentId == 0) return Deny<IReadOnlyList<LmsProgressGuardianGrantDto>>();
        var rows = await (from grant in db.LmsProgressGuardianGrants.AsNoTracking()
            join link in db.StudentGuardians.AsNoTracking() on grant.StudentGuardianId equals link.Id
            join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
            where grant.TenantId == user.TenantId && link.TenantId == user.TenantId &&
                guardian.TenantId == user.TenantId && grant.CourseEnrollmentId == enrollmentId &&
                link.StudentId == studentId && !grant.IsDeleted && grant.RevokedAt == null &&
                !link.IsDeleted && !guardian.IsDeleted && guardian.IsActive
            orderby grant.GrantedAt descending, grant.Id descending
            select new LmsProgressGuardianGrantDto
            {
                Reference = grant.PublicId, CourseEnrollmentId = grant.CourseEnrollmentId,
                GuardianLinkId = link.Id, GuardianName = guardian.FullName,
                GrantedAt = grant.GrantedAt, RowVersion = Convert.ToBase64String(grant.RowVersion)
            }).Take(25).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LmsProgressGuardianGrantDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<LmsProgressGuardianGrantDto>> GrantAsync(GrantLmsProgressGuardianRequestDto request, CancellationToken ct = default)
    {
        if (!Student) return Deny<LmsProgressGuardianGrantDto>();
        if (request == null || request.CourseEnrollmentId <= 0 ||
            request.GuardianLinkId <= 0 || request.ClientRequestId == Guid.Empty)
            return Invalid<LmsProgressGuardianGrantDto>();
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var enrollment = await Owned().FirstOrDefaultAsync(x => x.Id == request.CourseEnrollmentId, token);
                if (enrollment == null) return Deny<LmsProgressGuardianGrantDto>();
                var guardian = await EligibleLinks(enrollment.StudentId)
                    .FirstOrDefaultAsync(x => x.GuardianLinkId == request.GuardianLinkId, token);
                if (guardian == null) return Deny<LmsProgressGuardianGrantDto>();
                var previous = await db.LmsProgressGuardianGrants.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, token);
                if (previous != null)
                {
                    if (previous.CourseEnrollmentId != enrollment.Id || previous.StudentGuardianId != request.GuardianLinkId)
                        return ApiResponse<LmsProgressGuardianGrantDto>.ErrorResponse("Request reference conflict.", 409);
                    return ApiResponse<LmsProgressGuardianGrantDto>.SuccessResponse(Map(previous, guardian.GuardianName));
                }
                var active = await db.LmsProgressGuardianGrants.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.CourseEnrollmentId == enrollment.Id &&
                    x.StudentGuardianId == request.GuardianLinkId && !x.IsDeleted && x.RevokedAt == null, token);
                if (active != null) return ApiResponse<LmsProgressGuardianGrantDto>.SuccessResponse(Map(active, guardian.GuardianName));
                var now = clock.GetUtcNow().UtcDateTime;
                var grant = new LmsProgressGuardianGrant
                {
                    TenantId = user.TenantId, PublicId = Guid.NewGuid(),
                    ClientRequestId = request.ClientRequestId, CourseEnrollmentId = enrollment.Id,
                    StudentGuardianId = request.GuardianLinkId, GrantedAt = now,
                    GrantedByUserId = user.UserId, CreatedAt = now, CreatedBy = user.UserId
                };
                db.LmsProgressGuardianGrants.Add(grant);
                await db.SaveChangesAsync(token);
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId, EntityName = "LmsProgressGuardianGrant",
                    EntityId = grant.Id, Action = "LMS.ProgressGuardianGranted", OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<LmsProgressGuardianGrantDto>.SuccessResponse(Map(grant, guardian.GuardianName));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<LmsProgressGuardianGrantDto>.ErrorResponse("Consent changed concurrently.", 409); }
        catch (Exception ex) when (SqlConflict(ex))
        {
            logger.LogWarning(ex, "LMS progress consent SQL conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<LmsProgressGuardianGrantDto>.ErrorResponse("Concurrent consent change. Retry.", 409);
        }
    }

    public async Task<ApiResponse<LmsProgressGuardianGrantDto>> RevokeAsync(
        Guid reference, RevokeLmsProgressGuardianRequestDto request, CancellationToken ct = default)
    {
        if (!Student || reference == Guid.Empty) return Deny<LmsProgressGuardianGrantDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.RowVersion)) return Invalid<LmsProgressGuardianGrantDto>();
        byte[] version;
        try { version = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return Invalid<LmsProgressGuardianGrantDto>(); }
        if (version.Length != 8) return Invalid<LmsProgressGuardianGrantDto>();
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var grant = await db.LmsProgressGuardianGrants.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, token);
                if (grant == null) return Deny<LmsProgressGuardianGrantDto>();
                var enrollment = await Owned().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == grant.CourseEnrollmentId, token);
                if (enrollment == null) return Deny<LmsProgressGuardianGrantDto>();
                var guardian = await EligibleLinks(enrollment.StudentId)
                    .FirstOrDefaultAsync(x => x.GuardianLinkId == grant.StudentGuardianId, token);
                if (guardian == null) return Deny<LmsProgressGuardianGrantDto>();
                if (grant.RevokedAt.HasValue)
                    return ApiResponse<LmsProgressGuardianGrantDto>.SuccessResponse(Map(grant, guardian.GuardianName));
                if (!grant.RowVersion.SequenceEqual(version))
                    return ApiResponse<LmsProgressGuardianGrantDto>.ErrorResponse("Consent changed. Reload.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                grant.RevokedAt = now; grant.RevokedByUserId = user.UserId;
                grant.UpdatedAt = now; grant.UpdatedBy = user.UserId;
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId, EntityName = "LmsProgressGuardianGrant",
                    EntityId = grant.Id, Action = "LMS.ProgressGuardianRevoked", OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<LmsProgressGuardianGrantDto>.SuccessResponse(Map(grant, guardian.GuardianName));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<LmsProgressGuardianGrantDto>.ErrorResponse("Consent changed concurrently.", 409); }
        catch (Exception ex) when (SqlConflict(ex))
        {
            logger.LogWarning(ex, "LMS progress consent SQL conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<LmsProgressGuardianGrantDto>.ErrorResponse("Concurrent consent change. Retry.", 409);
        }
    }

    private IQueryable<CourseEnrollment> Shared() =>
        (from grant in db.LmsProgressGuardianGrants.AsNoTracking()
         join enrollment in db.CourseEnrollments.AsNoTracking() on grant.CourseEnrollmentId equals enrollment.Id
         join course in db.Courses.AsNoTracking() on enrollment.CourseId equals course.Id
         join learner in db.Students.AsNoTracking() on enrollment.StudentId equals learner.Id
         join link in db.StudentGuardians.AsNoTracking() on grant.StudentGuardianId equals link.Id
         join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
         where grant.TenantId == user.TenantId && enrollment.TenantId == user.TenantId &&
             course.TenantId == user.TenantId && learner.TenantId == user.TenantId &&
             link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
             guardian.UserId == user.UserId && guardian.IsActive && !guardian.IsDeleted &&
             link.StudentId == learner.Id && !link.IsDeleted && !learner.IsDeleted &&
             !course.IsDeleted && course.IsActive && !enrollment.IsDeleted &&
             (enrollment.State == CourseEnrollmentState.Active || enrollment.State == CourseEnrollmentState.Completed) &&
             !grant.IsDeleted && grant.RevokedAt == null
         select enrollment).Distinct();

    // One serializable read boundary covers consent, relationship, course and progress.
    // A revocation/deactivation cannot commit between authorization and disclosure.
    public async Task<ApiResponse<PagedResult<LmsGuardianCourseProgressDto>>> SharedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!Guardian) return Deny<PagedResult<LmsGuardianCourseProgressDto>>();
        if (page < 1 || pageSize is < 1 or > 20 || ((long)page - 1) * pageSize > int.MaxValue)
            return Invalid<PagedResult<LmsGuardianCourseProgressDto>>();
        try
        {
            return await db.ExecuteInTransactionAsync(
                token => SharedCoreAsync(page, pageSize, token), IsolationLevel.Serializable, ct);
        }
        catch (Exception ex) when (SqlConflict(ex))
        {
            logger.LogWarning(ex, "LMS guardian course read SQL conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<PagedResult<LmsGuardianCourseProgressDto>>.ErrorResponse(
                "Concurrent progress access change. Retry.", 409);
        }
    }

    private async Task<ApiResponse<PagedResult<LmsGuardianCourseProgressDto>>> SharedCoreAsync(
        int page, int pageSize, CancellationToken ct)
    {
        if (!Guardian) return Deny<PagedResult<LmsGuardianCourseProgressDto>>();
        if (page < 1 || pageSize is < 1 or > 20 || ((long)page - 1) * pageSize > int.MaxValue)
            return Invalid<PagedResult<LmsGuardianCourseProgressDto>>();
        var query = Shared();
        var count = await query.CountAsync(ct);
        var selected = await query.OrderByDescending(x => x.EnrolledAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.CourseId, x.StudentId, x.State }).ToListAsync(ct);
        var courseIds = selected.Select(x => x.CourseId).Distinct().ToArray();
        var enrollmentIds = selected.Select(x => x.Id).ToArray();
        var studentIds = selected.Select(x => x.StudentId).Distinct().ToArray();
        var courses = await db.Courses.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            courseIds.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, x.Title })
            .ToDictionaryAsync(x => x.Id, ct);
        var students = await db.Students.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            studentIds.Contains(x.Id)).Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, ct);
        var lessons = await db.Lessons.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            courseIds.Contains(x.CourseId) && x.IsPublished && !x.IsDeleted)
            .GroupBy(x => x.CourseId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var completed = await (
            from progress in db.LessonProgresses.AsNoTracking()
            join lesson in db.Lessons.AsNoTracking() on progress.LessonId equals lesson.Id
            join enrollment in db.CourseEnrollments.AsNoTracking() on progress.CourseEnrollmentId equals enrollment.Id
            where progress.TenantId == user.TenantId && lesson.TenantId == user.TenantId &&
                enrollment.TenantId == user.TenantId && enrollment.CourseId == lesson.CourseId &&
                enrollmentIds.Contains(progress.CourseEnrollmentId) &&
                !progress.IsDeleted && !lesson.IsDeleted && lesson.IsPublished &&
                progress.IsCompleted && progress.ProgressPercent >= 100m
            select new { progress.CourseEnrollmentId, progress.LessonId })
            .Distinct().GroupBy(x => x.CourseEnrollmentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var rows = selected.Where(x => courses.ContainsKey(x.CourseId) && students.ContainsKey(x.StudentId))
            .Select(x =>
            {
                var total = lessons.GetValueOrDefault(x.CourseId);
                var done = Math.Min(total, completed.GetValueOrDefault(x.Id));
                return new LmsGuardianCourseProgressDto
                {
                    CourseEnrollmentId = x.Id, CourseReference = courses[x.CourseId].PublicId,
                    CourseTitle = courses[x.CourseId].Title, LearnerName = students[x.StudentId].FullName,
                    State = x.State, PublishedLessons = total, CompletedLessons = done,
                    LessonCompletionPercent = total == 0 ? 0m :
                        Math.Round(100m * done / total, 2, MidpointRounding.AwayFromZero)
                };
            }).ToList();
        return ApiResponse<PagedResult<LmsGuardianCourseProgressDto>>.SuccessResponse(
            new PagedResult<LmsGuardianCourseProgressDto>
            { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }

    public async Task<ApiResponse<PagedResult<LessonProgressDto>>> LessonsAsync(
        long enrollmentId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Guardian || enrollmentId <= 0) return Deny<PagedResult<LessonProgressDto>>();
        if (page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue)
            return Invalid<PagedResult<LessonProgressDto>>();
        try
        {
            return await db.ExecuteInTransactionAsync(
                token => LessonsCoreAsync(enrollmentId, page, pageSize, token),
                IsolationLevel.Serializable, ct);
        }
        catch (Exception ex) when (SqlConflict(ex))
        {
            logger.LogWarning(ex, "LMS guardian lesson read SQL conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<PagedResult<LessonProgressDto>>.ErrorResponse(
                "Concurrent progress access change. Retry.", 409);
        }
    }

    private async Task<ApiResponse<PagedResult<LessonProgressDto>>> LessonsCoreAsync(
        long enrollmentId, int page, int pageSize, CancellationToken ct)
    {
        if (!Guardian || enrollmentId <= 0) return Deny<PagedResult<LessonProgressDto>>();
        if (page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue)
            return Invalid<PagedResult<LessonProgressDto>>();
        var enrollment = await Shared().Where(x => x.Id == enrollmentId)
            .Select(x => new { x.Id, x.CourseId }).FirstOrDefaultAsync(ct);
        if (enrollment == null) return Deny<PagedResult<LessonProgressDto>>();
        var query = db.Lessons.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseId == enrollment.CourseId && x.IsPublished && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var selected = await query.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.PublicId, x.Title }).ToListAsync(ct);
        var ids = selected.Select(x => x.Id).ToArray();
        var progress = await db.LessonProgresses.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.CourseEnrollmentId == enrollmentId && ids.Contains(x.LessonId) && !x.IsDeleted)
            .OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.LessonId, x.ProgressPercent, x.IsCompleted, x.CompletedAt })
            .ToListAsync(ct);
        var byLesson = progress.GroupBy(x => x.LessonId).ToDictionary(x => x.Key, x => x.First());
        var rows = selected.Select(x =>
        {
            byLesson.TryGetValue(x.Id, out var record);
            return new LessonProgressDto
            {
                Id = record?.Id ?? 0, CourseEnrollmentId = enrollmentId,
                LessonReference = x.PublicId, LessonTitle = x.Title,
                ProgressPercent = record?.ProgressPercent ?? 0m,
                IsCompleted = record?.IsCompleted ?? false, CompletedAt = record?.CompletedAt
            };
        }).ToList();
        return ApiResponse<PagedResult<LessonProgressDto>>.SuccessResponse(
            new PagedResult<LessonProgressDto>
            { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }

    private static LmsProgressGuardianGrantDto Map(LmsProgressGuardianGrant row, string name) => new()
    {
        Reference = row.PublicId, CourseEnrollmentId = row.CourseEnrollmentId,
        GuardianLinkId = row.StudentGuardianId, GuardianName = name,
        GrantedAt = row.GrantedAt, RowVersion = Convert.ToBase64String(row.RowVersion)
    };
}
