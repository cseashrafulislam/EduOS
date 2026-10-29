using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// SQL-backed certificate issue/revoke/reissue races on independent DbContexts.
/// The shared disposable tenant fixture applies real migrations. Never touches
/// customer records; the SQL test connection is opt-in for CI.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static LmsWorkflowService SqlCertificateWorkflow(
        EduOSDbContext db, long tenant, long userId, string role = "Principal") =>
        new(
            new GenericRepository<Course>(db),
            new GenericRepository<CourseEnrollment>(db),
            new GenericRepository<Lesson>(db),
            new GenericRepository<LessonProgress>(db),
            new GenericRepository<Assignment>(db),
            new GenericRepository<AssignmentSubmission>(db),
            new GenericRepository<Student>(db),
            new GenericRepository<StudentEnrollment>(db),
            new GenericRepository<Employee>(db),
            new GenericRepository<AcademicProgram>(db),
            new GenericRepository<Subject>(db),
            new GenericRepository<SubjectOffering>(db),
            new GenericRepository<CurriculumSubject>(db),
            new GenericRepository<FileAsset>(db),
            db, new CertificateActor(role, tenant, userId), TimeProvider.System,
            NullLogger<LmsWorkflowService>.Instance,
            completionQuizzes: new GenericRepository<Quiz>(db),
            completionAttempts: new GenericRepository<QuizAttempt>(db),
            certificates: new GenericRepository<LmsCompletionCertificate>(db),
            certificateAudits: new GenericRepository<AuditLog>(db));

    [Fact]
    public async Task SQL_LMS_certificate_issue_revoke_reissue_races_preserve_single_active_snapshot_and_audit()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var foreign = await SeedAsync(connection);
        long courseEnrollmentId;
        Guid certificateReference;
        Guid requestId = Guid.NewGuid();

        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await setup.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var learner = await setup.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            learner.UserId = fixture.UserId;
            var course = new Course
            {
                TenantId = fixture.TenantId,
                Code = "RACE-CERT-" + Guid.NewGuid().ToString("N")[..12],
                Title = "Original SQL course title", IsActive = true
            };
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();
            var enrollment = new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                StudentId = learner.Id, StudentEnrollmentId = academic.Id,
                ClientRequestId = Guid.NewGuid(),
                State = CourseEnrollmentState.Completed,
                EnrolledAt = DateTime.UtcNow.AddDays(-5),
                CompletedAt = DateTime.UtcNow.AddDays(-1)
            };
            var lesson = new Lesson
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                Title = "Published SQL lesson", IsPublished = true
            };
            setup.AddRange(enrollment, lesson);
            await setup.SaveChangesAsync();
            setup.LessonProgresses.Add(new LessonProgress
            {
                TenantId = fixture.TenantId, CourseEnrollmentId = enrollment.Id,
                LessonId = lesson.Id, IsCompleted = true, ProgressPercent = 100m
            });
            await setup.SaveChangesAsync();
            courseEnrollmentId = enrollment.Id;
        }

        var issueRequest = new IssueLmsCertificateRequestDto
        {
            CourseEnrollmentId = courseEnrollmentId, ClientRequestId = requestId
        };
        await using (var dbA = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        await using (var dbB = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var a = SqlCertificateWorkflow(dbA, fixture.TenantId, fixture.UserId);
            var b = SqlCertificateWorkflow(dbB, fixture.TenantId, fixture.UserId);
            var attempts = await Task.WhenAll(
                Task.Run(() => a.IssueCertificateAsync(issueRequest)),
                Task.Run(() => b.IssueCertificateAsync(issueRequest)));
            Assert.Contains(attempts, x => x.Success);
            Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));
        }

        string initialVersion;
        string originalCourseTitle;
        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var certificate = Assert.Single(await verify.LmsCompletionCertificates
                .Where(x => x.TenantId == fixture.TenantId && x.CourseEnrollmentId == courseEnrollmentId)
                .ToListAsync());
            certificateReference = certificate.PublicId;
            originalCourseTitle = certificate.CourseTitleSnapshot;
            initialVersion = Convert.ToBase64String(certificate.RowVersion);
            Assert.Equal(1, certificate.Revision);
            Assert.Equal(1, certificate.RequiredLessonsSnapshot);
            Assert.Equal(1, certificate.CompletedLessonsSnapshot);
            Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.EntityName == "LmsCompletionCertificate" &&
                x.EntityId == certificate.Id && x.Action == "LMS.CertificateIssued"));
            var replay = await SqlCertificateWorkflow(verify, fixture.TenantId, fixture.UserId)
                .IssueCertificateAsync(issueRequest);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(certificateReference, replay.Data!.Reference);
            var foreignRead = await SqlCertificateWorkflow(verify, foreign.TenantId, fixture.UserId)
                .GetCertificateAsync(certificateReference);
            Assert.Equal(404, foreignRead.StatusCode);
            var foreignIssue = await SqlCertificateWorkflow(verify, foreign.TenantId, fixture.UserId)
                .IssueCertificateAsync(new IssueLmsCertificateRequestDto
                {
                    CourseEnrollmentId = courseEnrollmentId, ClientRequestId = Guid.NewGuid()
                });
            Assert.Equal(404, foreignIssue.StatusCode);
        }

        var revokeRequest = new RevokeLmsCertificateRequestDto
        {
            Reason = "Verified SQL lifecycle correction", RowVersion = initialVersion
        };
        await using (var dbA = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        await using (var dbB = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var a = SqlCertificateWorkflow(dbA, fixture.TenantId, fixture.UserId);
            var b = SqlCertificateWorkflow(dbB, fixture.TenantId, fixture.UserId);
            var attempts = await Task.WhenAll(
                Task.Run(() => a.RevokeCertificateAsync(certificateReference, revokeRequest)),
                Task.Run(() => b.RevokeCertificateAsync(certificateReference, revokeRequest)));
            Assert.Contains(attempts, x => x.Success);
            Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));
        }

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var certificate = await verify.LmsCompletionCertificates.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == certificateReference);
            Assert.True(certificate.IsRevoked);
            Assert.NotNull(certificate.RevokedAt);
            Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.EntityName == "LmsCompletionCertificate" &&
                x.EntityId == certificate.Id && x.Action == "LMS.CertificateRevoked"));
            var stale = await SqlCertificateWorkflow(verify, fixture.TenantId, fixture.UserId)
                .VerifyCertificateAsync(certificateReference);
            Assert.True(stale.Success);
            Assert.False(stale.Data!.IsValid);
            var course = await verify.Courses.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == certificate.CourseId);
            course.Title = "Changed course title after issuance";
            await verify.SaveChangesAsync();
        }

        await using (var dbA = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        await using (var dbB = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var a = SqlCertificateWorkflow(dbA, fixture.TenantId, fixture.UserId);
            var b = SqlCertificateWorkflow(dbB, fixture.TenantId, fixture.UserId);
            var attempts = await Task.WhenAll(
                Task.Run(() => a.IssueCertificateAsync(new IssueLmsCertificateRequestDto
                {
                    CourseEnrollmentId = courseEnrollmentId, ClientRequestId = Guid.NewGuid()
                })),
                Task.Run(() => b.IssueCertificateAsync(new IssueLmsCertificateRequestDto
                {
                    CourseEnrollmentId = courseEnrollmentId, ClientRequestId = Guid.NewGuid()
                })));
            Assert.Contains(attempts, x => x.Success);
            Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));
        }

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var rows = await verify.LmsCompletionCertificates
                .Where(x => x.TenantId == fixture.TenantId && x.CourseEnrollmentId == courseEnrollmentId)
                .OrderBy(x => x.Revision).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].IsRevoked);
            Assert.False(rows[1].IsRevoked);
            Assert.Equal(2, rows[1].Revision);
            Assert.Equal(originalCourseTitle, rows[1].CourseTitleSnapshot);
            Assert.Equal(rows[0].StudentNameSnapshot, rows[1].StudentNameSnapshot);
            Assert.Equal(rows[0].CompletedAtSnapshot, rows[1].CompletedAtSnapshot);
            Assert.Equal(3, await verify.AuditLogs.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.EntityName == "LmsCompletionCertificate" &&
                (x.Action == "LMS.CertificateIssued" ||
                 x.Action == "LMS.CertificateRevoked" ||
                 x.Action == "LMS.CertificateReissued") &&
                (x.EntityId == rows[0].Id || x.EntityId == rows[1].Id)));
            var current = await SqlCertificateWorkflow(verify, fixture.TenantId, fixture.UserId)
                .VerifyCertificateAsync(rows[1].PublicId);
            Assert.True(current.Success);
            Assert.True(current.Data!.IsValid);
        }
    }
}
