using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsWorkflowService
{
    public async Task<ApiResponse<CourseContentPageDto>> GetCourseContentPageAsync(
        Guid courseReference, int lessonPage, int assignmentPage, int pageSize, CancellationToken ct = default)
    {
        if (!Authenticated() || courseReference == Guid.Empty)
            return Error<CourseContentPageDto>("Course not found.", 404);
        if (lessonPage < 1 || assignmentPage < 1 || pageSize is < 1 or > 100 ||
            ((long)lessonPage - 1) * pageSize > int.MaxValue ||
            ((long)assignmentPage - 1) * pageSize > int.MaxValue)
            return Error<CourseContentPageDto>("Invalid course content pagination.");
        var tenant = _user.TenantId;
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == courseReference && !x.IsDeleted, ct);
        if (course == null || !await CanViewAsync(course, ct))
            return Error<CourseContentPageDto>("Course not found.", 404);
        var canEdit = await CanEditAsync(course, ct);
        var lessons = _lessons.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && !x.IsDeleted &&
            (canEdit || x.IsPublished));
        var assignments = _assignments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && !x.IsDeleted &&
            (canEdit || x.IsPublished));
        var lessonTotal = await lessons.CountAsync(ct);
        var assignmentTotal = await assignments.CountAsync(ct);
        var lessonRows = await lessons.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Skip((int)(((long)lessonPage - 1) * pageSize)).Take(pageSize).ToListAsync(ct);
        var assignmentRows = await assignments.OrderBy(x => x.DueAt).ThenBy(x => x.Id)
            .Skip((int)(((long)assignmentPage - 1) * pageSize)).Take(pageSize).ToListAsync(ct);
        return ApiResponse<CourseContentPageDto>.SuccessResponse(new CourseContentPageDto
        {
            Course = await MapCourseAsync(course, ct),
            Lessons = new PagedResult<LessonDto>
            {
                Page = lessonPage, PageSize = pageSize, TotalCount = lessonTotal,
                Items = lessonRows.Select(x => MapLesson(x, course.PublicId)).ToList()
            },
            Assignments = new PagedResult<AssignmentDto>
            {
                Page = assignmentPage, PageSize = pageSize, TotalCount = assignmentTotal,
                Items = assignmentRows.Select(x => MapAssignment(x, course.PublicId)).ToList()
            }
        });
    }
}
