using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

// Real SQL authorization, ownership and revocation of a published LMS handout.
// Uses the existing disposable two-student / multi-institute SQL fixture.
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static LmsCourseContentService LmsResourceService(EduOSDbContext db,
        Fixture fixture, IFileUploadService storage, long? tenantOverride = null) => new(
        new GenericRepository<Course>(db),
        new GenericRepository<CourseSection>(db),
        new GenericRepository<Lesson>(db),
        new GenericRepository<LessonResource>(db),
        new GenericRepository<FileAsset>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<Student>(db),
        new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db),
        new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<EmployeeCampusAssignment>(db),
        db, new DigitalReader(tenantOverride ?? fixture.TenantId, fixture.UserId, "Student"),
        TimeProvider.System, NullLogger<LmsCourseContentService>.Instance, storage: storage);

    [Fact]
    public async Task V2_LMS_sql_resource_download_is_enrollment_scoped_and_revoked_after_cancellation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var bytes = System.Text.Encoding.UTF8.GetBytes("private published lesson handout");
        long resourceId, enrollmentId;
        Guid courseReference;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var student = await db.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            student.UserId = fixture.UserId;
            var course = new Course
            {
                TenantId = fixture.TenantId, Code = "LMS-RESOURCE-" + Guid.NewGuid().ToString("N")[..12],
                Title = "Authorized lesson handout", IsActive = true
            };
            db.Add(course); await db.SaveChangesAsync();
            courseReference = course.PublicId;
            var lesson = new Lesson
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                Title = "Published lesson", IsPublished = true
            };
            var asset = new FileAsset
            {
                TenantId = fixture.TenantId, StorageProvider = "LocalPrivate",
                StorageKey = $"tenant-{fixture.TenantId}/lms/verified.pdf",
                OriginalFileName = "verified.pdf", ContentType = "application/pdf",
                SizeBytes = bytes.Length,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                UploadedByUserId = fixture.UserId, MalwareScanStateCode = "Clean",
                Visibility = FileVisibility.Private, IsVerifiedSafe = true
            };
            var enrollment = new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                StudentId = student.Id, ClientRequestId = Guid.NewGuid(),
                State = CourseEnrollmentState.Active
            };
            db.AddRange(lesson, asset, enrollment); await db.SaveChangesAsync();
            var resource = new LessonResource
            {
                TenantId = fixture.TenantId, LessonId = lesson.Id,
                FileAssetId = asset.Id, Title = "Verified handout"
            };
            db.Add(resource); await db.SaveChangesAsync();
            resourceId = resource.Id; enrollmentId = enrollment.Id;
        }
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync($"tenant-{fixture.TenantId}/lms/verified.pdf"))
            .ReturnsAsync(new FileDownloadResult { Content = bytes });
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var service = LmsResourceService(db, fixture, storage.Object);
            var allowed = await service.DownloadResourceFileAsync(resourceId);
            Assert.True(allowed.Success, allowed.Message);
            Assert.Equal(bytes, allowed.Data!.Content);
            var page = await service.GetCourseContentAsync(courseReference);
            Assert.True(page.Success, page.Message);
            Assert.True(Assert.Single(page.Data!.UnsectionedLessons).Resources.Single().FileDownloadable);
            Assert.Equal(404, (await LmsResourceService(db, fixture, storage.Object, long.MaxValue)
                .DownloadResourceFileAsync(resourceId)).StatusCode);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var enrollment = await db.Set<CourseEnrollment>().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == enrollmentId);
            enrollment.State = CourseEnrollmentState.Cancelled;
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var denied = await LmsResourceService(db, fixture, storage.Object)
                .DownloadResourceFileAsync(resourceId);
            Assert.Equal(404, denied.StatusCode);
        }
    }
}
