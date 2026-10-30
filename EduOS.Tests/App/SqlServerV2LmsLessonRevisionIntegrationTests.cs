using EduOS.Core.Entities.LMS;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task LMS_SQL_lesson_revision_is_append_only_and_tenant_bound()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        long id;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var course = new Course
            {
                TenantId = fixture.TenantId, Code = "REV-" + Guid.NewGuid().ToString("N")[..12],
                Title = "Revision evidence"
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            var lesson = new Lesson { TenantId = fixture.TenantId, CourseId = course.Id, Title = "Original" };
            db.Lessons.Add(lesson);
            await db.SaveChangesAsync();
            var revision = new LmsLessonRevision
            {
                TenantId = fixture.TenantId, LessonId = lesson.Id, CourseId = course.Id,
                RevisionNo = 1, ChangeKind = "Lesson", Title = lesson.Title,
                ResourceSnapshotJson = "[]", CapturedAt = DateTime.UtcNow,
                CapturedByUserId = fixture.UserId
            };
            db.LmsLessonRevisions.Add(revision);
            await db.SaveChangesAsync();
            id = revision.Id;
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.LmsLessonRevisions SET Title = {"Changed"} WHERE Id = {id}"));
            await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                db.LmsLessonRevisions.Add(new LmsLessonRevision
                {
                    TenantId = fixture.TenantId, LessonId = lesson.Id, CourseId = course.Id,
                    RevisionNo = 1, ChangeKind = "Duplicate", Title = "Duplicate",
                    ResourceSnapshotJson = "[]", CapturedAt = DateTime.UtcNow,
                    CapturedByUserId = fixture.UserId
                });
                await db.SaveChangesAsync();
            });
        }
        await using (var other = ScopedContext(fixture.Options, fixture.TenantId + 999, fixture.UserId))
            Assert.False(await other.LmsLessonRevisions.AnyAsync(x => x.Id == id));
    }
}
