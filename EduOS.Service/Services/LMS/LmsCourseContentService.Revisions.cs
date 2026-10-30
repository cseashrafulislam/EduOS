using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsCourseContentService
{
    public async Task<ApiResponse<LmsLessonRevisionPageDto>> GetLessonRevisionsAsync(
        Guid lessonReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Author) return Fail<LmsLessonRevisionPageDto>("Author permission required.", 403);
        if (lessonReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 || _revisions == null)
            return Fail<LmsLessonRevisionPageDto>("Invalid lesson revision request.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var lesson = await _lessons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == lessonReference && !x.IsDeleted, token);
                if (lesson == null) return Fail<LmsLessonRevisionPageDto>("Lesson not found.", 404);
                var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == lesson.CourseId && !x.IsDeleted, token);
                if (course == null || !await CanEditAsync(course, token))
                    return Fail<LmsLessonRevisionPageDto>("Lesson not found.", 404);
                var query = _revisions.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == Tenant && x.LessonId == lesson.Id && x.CourseId == course.Id);
                var count = await query.CountAsync(token);
                if ((long)(page - 1) * pageSize > count && count != 0)
                    return Fail<LmsLessonRevisionPageDto>("Revision page not found.", 404);
                var rows = await query.OrderByDescending(x => x.RevisionNo)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(x => new { x.RevisionNo, x.ChangeKind, x.CapturedAt,
                        x.Title, x.Content, x.ContentUrl, x.CourseSectionId,
                        x.DisplayOrder, x.IsPublished, x.ResourceSnapshotJson })
                    .ToListAsync(token);
                return ApiResponse<LmsLessonRevisionPageDto>.SuccessResponse(new LmsLessonRevisionPageDto
                {
                    LessonReference = lessonReference, Page = page, PageSize = pageSize,
                    TotalCount = count, Items = rows.Select(x => new LmsLessonRevisionDto
                    {
                        RevisionNo = x.RevisionNo, ChangeKind = x.ChangeKind,
                        CapturedAt = x.CapturedAt, Title = x.Title, Content = x.Content,
                        ContentUrl = x.ContentUrl, CourseSectionId = x.CourseSectionId,
                        DisplayOrder = x.DisplayOrder, IsPublished = x.IsPublished,
                        Resources = LmsLessonRevisionWriter.PublicResources(x.ResourceSnapshotJson)
                    }).ToList()
                });
            }, IsolationLevel.Serializable, ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960)
        {
            _logger.LogWarning(ex, "LMS revision read conflict for tenant {Tenant}", Tenant);
            return Fail<LmsLessonRevisionPageDto>("Revision history changed concurrently. Retry.", 409);
        }
    }
}
