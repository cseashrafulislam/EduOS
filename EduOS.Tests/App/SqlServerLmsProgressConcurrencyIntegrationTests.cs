using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server acceptance of learner progress versus completion and withdrawal.
/// Uses independent DbContexts and the disposable, migrated tenant fixture.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task SQL_LMS_progress_duplicate_writes_remain_monotonic_and_completion_blocks_late_writes()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var foreign = await SeedAsync(connection);
        long enrollmentId;
        Guid lessonReference;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await setup.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var student = await setup.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            student.UserId = fixture.UserId;
            var course = new Course
            {
                TenantId = fixture.TenantId,
                Code = "PROGRESS-RACE-" + Guid.NewGuid().ToString("N")[..12],
                Title = "SQL progress and completion race", IsActive = true,
                IsSelfPaced = true
            };
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();
            var enrollment = new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active,
                EnrolledAt = DateTime.UtcNow.AddDays(-1)
            };
            var lesson = new Lesson
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                Title = "Published progress evidence", IsPublished = true
            };
            setup.AddRange(enrollment, lesson);
            await setup.SaveChangesAsync();
            enrollmentId = enrollment.Id;
            lessonReference = lesson.PublicId;
        }

        {
            // HttpContextAccessor uses an AsyncLocal holder: create each scoped
            // context inside its task, not in the parent before Task.Run.
            async Task<EduOS.Core.Common.ApiResponse<LessonProgressDto>> WriteAsync(decimal percent)
            {
                await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
                return await SqlCertificateWorkflow(db, fixture.TenantId, fixture.UserId, "Student")
                    .UpdateLessonProgressAsync(new UpdateLessonProgressRequestDto
                    { LessonReference = lessonReference, ProgressPercent = percent });
            }
            var attempts = await Task.WhenAll(
                Task.Run(() => WriteAsync(30m)), Task.Run(() => WriteAsync(80m)));
            Assert.Contains(attempts, x => x.Success);
            Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));
        }

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var progress = Assert.Single(await verify.LessonProgresses.AsNoTracking()
                .Where(x => x.TenantId == fixture.TenantId && x.CourseEnrollmentId == enrollmentId)
                .ToListAsync());
            Assert.InRange(progress.ProgressPercent, 30m, 80m);
            var learner = SqlCertificateWorkflow(verify, fixture.TenantId, fixture.UserId, "Student");
            var finish = await learner.UpdateLessonProgressAsync(new UpdateLessonProgressRequestDto
                { LessonReference = lessonReference, ProgressPercent = 100m, IsCompleted = true });
            Assert.True(finish.Success, finish.Message);
            var replay = await learner.UpdateLessonProgressAsync(new UpdateLessonProgressRequestDto
                { LessonReference = lessonReference, ProgressPercent = 1m });
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.IsCompleted);
            Assert.Equal(100m, replay.Data.ProgressPercent);
            var foreignRead = await SqlCertificateWorkflow(
                verify, foreign.TenantId, fixture.UserId, "Student")
                .UpdateLessonProgressAsync(new UpdateLessonProgressRequestDto
                    { LessonReference = lessonReference, ProgressPercent = 100m });
            Assert.Equal(404, foreignRead.StatusCode);
        }

        string rowVersion;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var enrollment = await db.CourseEnrollments.AsNoTracking().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == enrollmentId);
            rowVersion = Convert.ToBase64String(enrollment.RowVersion);
        }
        {
            async Task<EduOS.Core.Common.ApiResponse<CourseCompletionRegisterRowDto>> CompleteAsync()
            {
                await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
                return await SqlCertificateWorkflow(db, fixture.TenantId, fixture.UserId)
                    .CompleteCourseEnrollmentAsync(enrollmentId,
                        new CompleteCourseEnrollmentRequestDto { RowVersion = rowVersion });
            }
            async Task<EduOS.Core.Common.ApiResponse<LessonProgressDto>> ProgressAsync()
            {
                await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
                return await SqlCertificateWorkflow(db, fixture.TenantId, fixture.UserId, "Student")
                    .UpdateLessonProgressAsync(new UpdateLessonProgressRequestDto
                    { LessonReference = lessonReference, ProgressPercent = 10m });
            }
            var completionTask = Task.Run(CompleteAsync);
            var progressTask = Task.Run(ProgressAsync);
            await Task.WhenAll(completionTask, progressTask);
            var completion = await completionTask;
            var progress = await progressTask;
            Assert.True(completion.Success || completion.StatusCode == 409, completion.Message);
            Assert.True(progress.Success || progress.StatusCode == 409, progress.Message);
        }

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var enrollment = await verify.CourseEnrollments.AsNoTracking().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == enrollmentId);
            if (enrollment.State == CourseEnrollmentState.Active)
            {
                var retry = await SqlCertificateWorkflow(verify, fixture.TenantId, fixture.UserId)
                    .CompleteCourseEnrollmentAsync(enrollmentId,
                        new CompleteCourseEnrollmentRequestDto
                        { RowVersion = Convert.ToBase64String(enrollment.RowVersion) });
                Assert.True(retry.Success, retry.Message);
            }
        }
        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            Assert.Equal(CourseEnrollmentState.Completed,
                (await verify.CourseEnrollments.AsNoTracking().SingleAsync(x =>
                    x.TenantId == fixture.TenantId && x.Id == enrollmentId)).State);
            var late = await SqlCertificateWorkflow(verify, fixture.TenantId, fixture.UserId, "Student")
                .UpdateLessonProgressAsync(new UpdateLessonProgressRequestDto
                { LessonReference = lessonReference, ProgressPercent = 100m });
            Assert.Equal(409, late.StatusCode);
            var saved = Assert.Single(await verify.LessonProgresses.AsNoTracking()
                .Where(x => x.TenantId == fixture.TenantId && x.CourseEnrollmentId == enrollmentId)
                .ToListAsync());
            Assert.True(saved.IsCompleted);
            Assert.Equal(100m, saved.ProgressPercent);
        }
    }

    [Fact]
    public async Task SQL_LMS_cancelled_enrollment_denies_progress_even_after_preexisting_read()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        long enrollmentId;
        Guid lessonReference;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await setup.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var student = await setup.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            student.UserId = fixture.UserId;
            var course = new Course
            {
                TenantId = fixture.TenantId,
                Code = "CANCEL-RACE-" + Guid.NewGuid().ToString("N")[..12],
                Title = "SQL cancelled progress", IsActive = true, IsSelfPaced = true
            };
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();
            var enrollment = new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active
            };
            var lesson = new Lesson
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                Title = "Published lesson", IsPublished = true
            };
            setup.AddRange(enrollment, lesson);
            await setup.SaveChangesAsync();
            enrollmentId = enrollment.Id;
            lessonReference = lesson.PublicId;
        }

        // Keep the cancellation's exclusive SQL row lock until the progress request
        // is in flight; the service must recheck the state inside its transaction.
        await using (var cancelDb = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            await using var tx = await cancelDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var enrollment = await cancelDb.CourseEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == enrollmentId);
            enrollment.State = CourseEnrollmentState.Cancelled;
            await cancelDb.SaveChangesAsync();
            var progressTask = Task.Run(async () =>
            {
                await using var progressDb = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
                return await SqlCertificateWorkflow(progressDb, fixture.TenantId, fixture.UserId, "Student")
                    .UpdateLessonProgressAsync(new UpdateLessonProgressRequestDto
                    { LessonReference = lessonReference, ProgressPercent = 100m, IsCompleted = true });
            });
            await Task.Delay(150);
            await tx.CommitAsync();
            var progress = await progressTask;
            Assert.Equal(409, progress.StatusCode);
        }

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            Assert.Equal(CourseEnrollmentState.Cancelled,
                (await verify.CourseEnrollments.AsNoTracking().SingleAsync(x =>
                    x.TenantId == fixture.TenantId && x.Id == enrollmentId)).State);
            Assert.Equal(0, await verify.LessonProgresses.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.CourseEnrollmentId == enrollmentId));
        }
    }
}
