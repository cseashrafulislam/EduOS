using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Helpers.Storage;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Data;
using Xunit;

namespace EduOS.Tests.App;

// Real SQL concurrency checks against the same course-content read used by
// the student page and the server-generated CSV. No mock authorization.
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task LMS_SQL_course_content_denies_enrollment_cancelled_during_competing_read()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        Guid reference;
        long enrollmentId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var student = await db.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            student.UserId = fixture.UserId;
            var course = new Course
            {
                TenantId = fixture.TenantId, Code = "CONTENT-RACE-" + Guid.NewGuid().ToString("N")[..12],
                Title = "Enrollment withdrawal", IsActive = true
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            reference = course.PublicId;
            var enrollment = new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active
            };
            db.AddRange(enrollment, new Lesson
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                Title = "Private lesson", IsPublished = true
            });
            await db.SaveChangesAsync();
            enrollmentId = enrollment.Id;
        }
        await using (var withdraw = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            await using var transaction = await withdraw.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var enrollment = await withdraw.CourseEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == enrollmentId);
            enrollment.State = CourseEnrollmentState.Cancelled;
            await withdraw.SaveChangesAsync();
            var read = Task.Run(async () =>
            {
                await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
                return await LmsResourceService(db, fixture, new Mock<IFileUploadService>().Object)
                    .GetCourseContentAsync(reference);
            });
            await Task.Delay(150);
            await transaction.CommitAsync();
            var result = await read;
            Assert.Equal(404, result.StatusCode);
        }
    }

    [Fact]
    public async Task LMS_SQL_course_content_excludes_lesson_withdrawn_during_competing_read()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        Guid reference;
        long lessonId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var student = await db.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            student.UserId = fixture.UserId;
            var course = new Course
            {
                TenantId = fixture.TenantId, Code = "PUBLISH-RACE-" + Guid.NewGuid().ToString("N")[..12],
                Title = "Publication withdrawal", IsActive = true
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            reference = course.PublicId;
            var lesson = new Lesson
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                Title = "Withdrawn lesson", IsPublished = true
            };
            db.AddRange(new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active
            }, lesson);
            await db.SaveChangesAsync();
            lessonId = lesson.Id;
        }
        await using (var withdraw = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            await using var transaction = await withdraw.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var lesson = await withdraw.Lessons.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == lessonId);
            lesson.IsPublished = false;
            await withdraw.SaveChangesAsync();
            var read = Task.Run(async () =>
            {
                await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
                return await LmsResourceService(db, fixture, new Mock<IFileUploadService>().Object)
                    .GetCourseContentAsync(reference);
            });
            await Task.Delay(150);
            await transaction.CommitAsync();
            var result = await read;
            Assert.True(result.Success, result.Message);
            Assert.Empty(result.Data!.UnsectionedLessons);
            Assert.Empty(result.Data.Sections);
        }
    }
}
