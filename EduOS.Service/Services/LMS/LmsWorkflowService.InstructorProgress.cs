using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsWorkflowService
{
    // The register is the authority for selecting an enrollment. Recheck that authority
    // for every detail page and export; an enrollment ID alone grants no access.
    public Task<ApiResponse<PagedResult<LessonProgressDto>>> GetInstructorLessonProgressAsync(
        Guid courseReference, long enrollmentId, int page, int pageSize,
        CancellationToken cancellationToken = default) =>
        GetInstructorLessonProgressCoreAsync(courseReference, enrollmentId, page, pageSize, false, cancellationToken);

    public Task<ApiResponse<PagedResult<LessonProgressDto>>> GetInstructorLessonProgressExportAsync(
        Guid courseReference, long enrollmentId, CancellationToken cancellationToken = default) =>
        GetInstructorLessonProgressCoreAsync(courseReference, enrollmentId, 1, 250, true, cancellationToken);

    private async Task<ApiResponse<PagedResult<LessonProgressDto>>> GetInstructorLessonProgressCoreAsync(
        Guid courseReference, long enrollmentId, int page, int pageSize, bool export, CancellationToken ct)
    {
        if (!CanTeach())
            return Error<PagedResult<LessonProgressDto>>("LMS instructor permission required.", 403);
        if (courseReference == Guid.Empty || enrollmentId <= 0 || page < 1 ||
            pageSize < 1 || pageSize > (export ? 250 : 50) ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<LessonProgressDto>>("Invalid course, enrollment or pagination.");

        var course = await EditableCourseAsync(courseReference, ct);
        if (course == null)
            return Error<PagedResult<LessonProgressDto>>("Course not found or instructor access denied.", 404);

        var tenant = _user.TenantId;
        var allowed = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            where enrollment.TenantId == tenant && student.TenantId == tenant &&
                  enrollment.Id == enrollmentId && enrollment.CourseId == course.Id &&
                  !enrollment.IsDeleted && !student.IsDeleted &&
                  (enrollment.State == CourseEnrollmentState.Active ||
                   enrollment.State == CourseEnrollmentState.Completed) &&
                  (course.IsActive || enrollment.State == CourseEnrollmentState.Completed)
            select enrollment.Id).AnyAsync(ct);
        if (!allowed)
            return Error<PagedResult<LessonProgressDto>>("Course enrollment not found.", 404);

        // A learner's draft or unpublished lesson must not appear in a report or CSV.
        var lessons = _lessons.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted);
        var count = await lessons.CountAsync(ct);
        if (export && count > 250)
            return Error<PagedResult<LessonProgressDto>>(
                "More than 250 published lessons. Export a smaller course report.", 409);

        var selected = await lessons.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Skip((int)(((long)page - 1) * pageSize)).Take(pageSize)
            .Select(x => new { x.Id, x.PublicId, x.Title }).ToListAsync(ct);
        if (export && (selected.Count != count || await lessons.CountAsync(ct) != count))
            return Error<PagedResult<LessonProgressDto>>(
                "Lesson publication changed during export. Retry.", 409);

        var lessonIds = selected.Select(x => x.Id).ToArray();
        var saved = await _progress.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseEnrollmentId == enrollmentId &&
            lessonIds.Contains(x.LessonId) && !x.IsDeleted)
            .OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.LessonId, x.ProgressPercent, x.IsCompleted, x.CompletedAt })
            .ToListAsync(ct);
        var byLesson = saved.GroupBy(x => x.LessonId).ToDictionary(x => x.Key, x => x.First());
        var rows = selected.Select(x =>
        {
            byLesson.TryGetValue(x.Id, out var progress);
            return new LessonProgressDto
            {
                Id = progress?.Id ?? 0,
                CourseEnrollmentId = enrollmentId,
                LessonReference = x.PublicId,
                LessonTitle = x.Title,
                ProgressPercent = progress?.ProgressPercent ?? 0m,
                IsCompleted = progress?.IsCompleted ?? false,
                CompletedAt = progress?.CompletedAt
            };
        }).ToList();
        return ApiResponse<PagedResult<LessonProgressDto>>.SuccessResponse(
            new PagedResult<LessonProgressDto>
            { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }
}
