using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server V1 Student -> V2 LMS private-file metadata boundary.
/// Reuses the disposable two-student, campus and tenant fixture. Each test
/// uses independently scoped DbContexts so AsyncLocal tenant state cannot bleed.
/// No customer database or external object storage is accessed.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static LmsWorkflowService LmsFileBoundaryService(
        EduOSDbContext db, Fixture fixture) => new(
        new GenericRepository<Course>(db),
        new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db),
        new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db),
        new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<EduOS.Core.Entities.Students.Student>(db),
        new GenericRepository<EduOS.Core.Entities.Academic.StudentEnrollment>(db),
        new GenericRepository<EduOS.Core.Entities.HR.Employee>(db),
        new GenericRepository<EduOS.Core.Entities.Academic.AcademicProgram>(db),
        new GenericRepository<EduOS.Core.Entities.Academic.Subject>(db),
        new GenericRepository<EduOS.Core.Entities.Academic.SubjectOffering>(db),
        new GenericRepository<EduOS.Core.Entities.Academic.CurriculumSubject>(db),
        new GenericRepository<FileAsset>(db),
        db, new DigitalReader(fixture.TenantId, fixture.UserId, "Student"),
        TimeProvider.System, NullLogger<LmsWorkflowService>.Instance,
        new GenericRepository<AssignmentSubmissionFile>(db));

    private static async Task<(long OwnSubmissionId, long OtherSubmissionId, Guid OwnFile, Guid OtherFile)>
        SeedLmsPrivateSubmissionsAsync(Fixture fixture)
    {
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var academic = await db.StudentEnrollments.AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId &&
                fixture.Enrollments.Contains(x.PublicId))
            .OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(2, academic.Length);
        var owner = await db.Students.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.Id == academic[0].StudentId);
        owner.UserId = fixture.UserId;
        var course = new Course
        {
            TenantId = fixture.TenantId, Code = "CI-LMS-" + Guid.NewGuid().ToString("N")[..12],
            Title = "Private assignment file boundary", IsActive = true
        };
        db.Add(course);
        await db.SaveChangesAsync();
        var enrolled = academic.Select(x => new CourseEnrollment
        {
            TenantId = fixture.TenantId, CourseId = course.Id,
            StudentId = x.StudentId, StudentEnrollmentId = x.Id,
            ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active
        }).ToArray();
        var assignment = new Assignment
        {
            TenantId = fixture.TenantId, CourseId = course.Id,
            Title = "Private assignment", IsPublished = true, MaxMarks = 100m
        };
        var assets = academic.Select((x, i) => new FileAsset
        {
            TenantId = fixture.TenantId, PublicId = Guid.NewGuid(),
            UploadedByUserId = fixture.UserId,
            StorageProvider = "LocalPrivate",
            StorageKey = $"tenant-{fixture.TenantId}/documents/{Guid.NewGuid():N}.pdf",
            OriginalFileName = $"private-{i}.pdf", ContentType = "application/pdf",
            SizeBytes = 10, IsVerifiedSafe = true, MalwareScanStateCode = "Clean",
            Visibility = FileVisibility.Private
        }).ToArray();
        db.AddRange(enrolled);
        db.Add(assignment);
        db.AddRange(assets);
        await db.SaveChangesAsync();
        var submissions = enrolled.Select((x, i) => new AssignmentSubmission
        {
            TenantId = fixture.TenantId, AssignmentId = assignment.Id,
            CourseEnrollmentId = x.Id, FileAssetId = assets[i].Id,
            State = LearningSubmissionState.Submitted,
            SubmittedAt = DateTime.UtcNow
        }).ToArray();
        db.AddRange(submissions);
        await db.SaveChangesAsync();
        return (submissions[0].Id, submissions[1].Id,
            assets[0].PublicId, assets[1].PublicId);
    }

    [Fact]
    public async Task V2_LMS_sql_direct_submission_id_probes_never_disclose_other_student_or_tenant_file()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var first = await SeedAsync(connection);
        var second = await SeedAsync(connection);
        var own = await SeedLmsPrivateSubmissionsAsync(first);
        var foreign = await SeedLmsPrivateSubmissionsAsync(second);

        await using (var db = ScopedContext(first.Options, first.TenantId, first.UserId))
        {
            var service = LmsFileBoundaryService(db, first);
            var permitted = await service.GetSubmissionFilesAsync(own.OwnSubmissionId);
            Assert.True(permitted.Success, permitted.Message);
            Assert.Equal(own.OwnFile, Assert.Single(permitted.Data!).FileAssetReference);
            Assert.Equal(403, (await service.GetSubmissionFilesAsync(own.OtherSubmissionId)).StatusCode);
            Assert.Equal(404, (await service.GetSubmissionFilesAsync(foreign.OwnSubmissionId)).StatusCode);
            Assert.Equal(404, (await service.GetSubmissionFilesAsync(foreign.OtherSubmissionId)).StatusCode);
            Assert.Equal(404, (await service.GetSubmissionFilesAsync(long.MaxValue)).StatusCode);
        }
        await using (var db = ScopedContext(second.Options, second.TenantId, second.UserId))
        {
            var service = LmsFileBoundaryService(db, second);
            Assert.Equal(foreign.OwnFile,
                Assert.Single((await service.GetSubmissionFilesAsync(foreign.OwnSubmissionId)).Data!)
                    .FileAssetReference);
            Assert.Equal(404, (await service.GetSubmissionFilesAsync(own.OwnSubmissionId)).StatusCode);
        }
    }

    [Fact]
    public async Task V1_student_exit_revokes_V2_LMS_private_file_metadata_without_deleting_historical_submission()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var data = await SeedLmsPrivateSubmissionsAsync(fixture);
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            Assert.True((await LmsFileBoundaryService(db, fixture)
                .GetSubmissionFilesAsync(data.OwnSubmissionId)).Success);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            academic.IsCurrent = false;
            academic.State = EnrollmentState.Withdrawn;
            var student = await db.Students.SingleAsync(x => x.Id == academic.StudentId);
            student.StatusCode = "Transferred";
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var denied = await LmsFileBoundaryService(db, fixture)
                .GetSubmissionFilesAsync(data.OwnSubmissionId);
            Assert.Equal(403, denied.StatusCode);
            Assert.False(denied.Success);
            Assert.Equal(1, await db.Set<AssignmentSubmission>().CountAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == data.OwnSubmissionId &&
                x.State == LearningSubmissionState.Submitted && !x.IsDeleted));
        }
    }

    [Fact]
    public async Task V2_LMS_sql_removed_or_unverified_asset_is_not_exposed_even_by_its_owner()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var data = await SeedLmsPrivateSubmissionsAsync(fixture);
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var file = await db.FileAssets.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == data.OwnFile);
            file.IsVerifiedSafe = false;
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var files = await LmsFileBoundaryService(db, fixture)
                .GetSubmissionFilesAsync(data.OwnSubmissionId);
            Assert.True(files.Success, files.Message);
            Assert.Empty(files.Data!);
        }
    }

    [Fact]
    public async Task V1_academic_withdrawal_revokes_active_LMS_files_but_preserves_completed_history()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var data = await SeedLmsPrivateSubmissionsAsync(fixture);
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            academic.IsCurrent = false;
            academic.State = EnrollmentState.Withdrawn;
            // Student status remains Active during the lifecycle transition.
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var denied = await LmsFileBoundaryService(db, fixture)
                .GetSubmissionFilesAsync(data.OwnSubmissionId);
            Assert.Equal(403, denied.StatusCode);
            Assert.False(denied.Success);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var submission = await db.Set<AssignmentSubmission>().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == data.OwnSubmissionId);
            var enrollment = await db.Set<CourseEnrollment>().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == submission.CourseEnrollmentId);
            enrollment.State = CourseEnrollmentState.Completed;
            enrollment.CompletedAt = DateTime.UtcNow;
            var academic = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == enrollment.StudentEnrollmentId);
            academic.State = EnrollmentState.Completed;
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var history = await LmsFileBoundaryService(db, fixture)
                .GetSubmissionFilesAsync(data.OwnSubmissionId);
            Assert.True(history.Success, history.Message);
            Assert.Equal(data.OwnFile, Assert.Single(history.Data!).FileAssetReference);
        }
    }
}
