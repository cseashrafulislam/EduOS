using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LmsCertificateLifecycleTests
{
    [Fact]
    public async Task Issue_is_tenant_scoped_idempotent_and_requires_completed_evidence()
    {
        await using var db = Db();
        var (course, student, enrollment, lesson) = await Seed(db);
        var service = Service(db);
        var requestId = Guid.NewGuid();
        var request = new IssueLmsCertificateRequestDto
        {
            CourseEnrollmentId = enrollment.Id, ClientRequestId = requestId
        };
        Assert.Equal(409, (await service.IssueCertificateAsync(request)).StatusCode);
        enrollment.State = CourseEnrollmentState.Completed;
        enrollment.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(409, (await service.IssueCertificateAsync(request)).StatusCode);
        db.Add(new LessonProgress
        {
            TenantId = 101, CourseEnrollmentId = enrollment.Id, LessonId = lesson.Id,
            IsCompleted = true, ProgressPercent = 100m
        });
        await db.SaveChangesAsync();

        var issued = await service.IssueCertificateAsync(request);
        Assert.True(issued.Success, issued.Message);
        Assert.Equal(1, issued.Data!.Revision);
        Assert.Equal(1, issued.Data.RequiredLessons);
        Assert.Equal(1, issued.Data.CompletedLessons);
        Assert.True((await service.IssueCertificateAsync(request)).Success);
        var anotherRequest = await service.IssueCertificateAsync(new IssueLmsCertificateRequestDto
        {
            CourseEnrollmentId = enrollment.Id, ClientRequestId = Guid.NewGuid()
        });
        Assert.Equal(issued.Data.Reference, anotherRequest.Data!.Reference);
        Assert.Equal(1, await db.Set<LmsCompletionCertificate>().CountAsync());
        var audit = await db.Set<AuditLog>().SingleAsync(x => x.Action == "LMS.CertificateIssued");
        Assert.Equal(101, audit.TenantId);
        Assert.Equal(77, audit.UserId);
        Assert.Null(audit.OldValue);
        Assert.Null(audit.NewValue);
        Assert.Single((await service.GetCertificateAuditAsync(issued.Data.Reference)).Data!);
        Assert.Equal(409, (await service.IssueCertificateAsync(new IssueLmsCertificateRequestDto
        {
            CourseEnrollmentId = enrollment.Id + 1000, ClientRequestId = requestId
        })).StatusCode);
        Assert.Equal(403, (await Service(db, "Student").IssueCertificateAsync(request)).StatusCode);
        Assert.Equal(404, (await Service(db, "Teacher").IssueCertificateAsync(
            new IssueLmsCertificateRequestDto
            { CourseEnrollmentId = enrollment.Id, ClientRequestId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Revocation_and_reissue_preserve_immutable_evidence_and_audit_history()
    {
        await using var db = Db();
        var (course, student, enrollment, lesson) = await Seed(db, completed: true);
        var service = Service(db);
        var issued = await service.IssueCertificateAsync(new IssueLmsCertificateRequestDto
        { CourseEnrollmentId = enrollment.Id, ClientRequestId = Guid.NewGuid() });
        Assert.True(issued.Success, issued.Message);
        var first = await db.Set<LmsCompletionCertificate>().SingleAsync();
        first.RowVersion = new byte[] { 1, 2, 3 };
        await db.SaveChangesAsync();
        var stale = await service.RevokeCertificateAsync(first.PublicId,
            new RevokeLmsCertificateRequestDto { Reason = "Incorrect award", RowVersion = "AQ==" });
        Assert.Equal(409, stale.StatusCode);
        var revoked = await service.RevokeCertificateAsync(first.PublicId,
            new RevokeLmsCertificateRequestDto
            {
                Reason = "Incorrect award", RowVersion = Convert.ToBase64String(first.RowVersion)
            });
        Assert.True(revoked.Success, revoked.Message);
        Assert.True(revoked.Data!.IsRevoked);
        Assert.NotNull(revoked.Data.RevokedAt);
        Assert.Equal(77, (await db.Set<LmsCompletionCertificate>().SingleAsync()).RevokedByUserId);
        Assert.False((await service.VerifyCertificateAsync(first.PublicId)).Data!.IsValid);

        course.Title = "Renamed after completion";
        student.FullName = "Renamed student";
        await db.SaveChangesAsync();
        var reissued = await service.IssueCertificateAsync(new IssueLmsCertificateRequestDto
        { CourseEnrollmentId = enrollment.Id, ClientRequestId = Guid.NewGuid() });
        Assert.True(reissued.Success, reissued.Message);
        Assert.Equal(2, reissued.Data!.Revision);
        Assert.Equal(issued.Data!.CourseTitle, reissued.Data.CourseTitle);
        Assert.Equal(issued.Data.StudentName, reissued.Data.StudentName);
        Assert.Equal(issued.Data.CompletedAt, reissued.Data.CompletedAt);
        Assert.Equal(2, await db.Set<LmsCompletionCertificate>().CountAsync());
        Assert.Equal(3, await db.Set<AuditLog>().CountAsync(x =>
            x.EntityName == "LmsCompletionCertificate" && x.Action.StartsWith("LMS.Certificate")));
        var events = await service.GetCertificateAuditAsync(issued.Data.Reference);
        Assert.True(events.Success);
        Assert.Contains(events.Data!, x => x.Action == "LMS.CertificateRevoked");
        Assert.Equal(404, (await Service(db, "TenantAdmin", 202).GetCertificateAuditAsync(
            issued.Data.Reference)).StatusCode);
        Assert.Equal(403, (await Service(db, "Student").GetCertificateAuditAsync(
            issued.Data.Reference)).StatusCode);
        Assert.True((await service.VerifyCertificateAsync(reissued.Data.Reference)).Data!.IsValid);
        Assert.False((await service.VerifyCertificateAsync(first.PublicId)).Data!.IsValid);
    }

    [Fact]
    public async Task Direct_id_reads_and_paginated_registers_enforce_student_and_instructor_ownership()
    {
        await using var db = Db();
        var (course, _, enrollment, _) = await Seed(db, completed: true);
        var issued = await Service(db).IssueCertificateAsync(new IssueLmsCertificateRequestDto
        { CourseEnrollmentId = enrollment.Id, ClientRequestId = Guid.NewGuid() });
        Assert.True(issued.Success, issued.Message);
        var reference = issued.Data!.Reference;
        Assert.True((await Service(db, "Student").GetCertificateAsync(reference)).Success);
        Assert.Single((await Service(db, "Student").GetMyCertificatesAsync(1, 20)).Data!.Items);
        Assert.Equal(404, (await Service(db, "Student", 101, 888).GetCertificateAsync(reference)).StatusCode);
        Assert.Empty((await Service(db, "Student", 101, 888).GetMyCertificatesAsync(1, 20)).Data!.Items);
        Assert.Equal(404, (await Service(db, "TenantAdmin", 202).GetCertificateAsync(reference)).StatusCode);
        Assert.Equal(404, (await Service(db, "TenantAdmin", 202).GetCourseCertificatesAsync(
            course.PublicId, 1, 20)).StatusCode);
        Assert.Equal(404, (await Service(db, "Teacher").GetCertificateAsync(reference)).StatusCode);
        Assert.Equal(403, (await Service(db, "Student").GetCourseCertificatesAsync(
            course.PublicId, 1, 20)).StatusCode);
        Assert.Equal(400, (await Service(db).GetCourseCertificatesAsync(course.PublicId, 1, 26)).StatusCode);
        Assert.Single((await Service(db).GetCourseCertificatesAsync(course.PublicId, 1, 20)).Data!.Items);
    }

    private static async Task<(Course Course, Student Student, CourseEnrollment Enrollment, Lesson Lesson)> Seed(
        EduOSDbContext db, bool completed = false)
    {
        var course = new Course { TenantId = 101, Code = "CERT-1", Title = "Certificate Course", IsActive = true };
        var student = new Student
        {
            TenantId = 101, StudentCode = "S-101", FullName = "Learner One",
            UserId = 77, StatusCode = "Active"
        };
        db.AddRange(course, student); await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment
        {
            TenantId = 101, CourseId = course.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), EnrolledAt = DateTime.UtcNow.AddDays(-10),
            State = completed ? CourseEnrollmentState.Completed : CourseEnrollmentState.Active,
            CompletedAt = completed ? DateTime.UtcNow.AddDays(-1) : null
        };
        var lesson = new Lesson
        {
            TenantId = 101, CourseId = course.Id, Title = "Lesson",
            IsPublished = true
        };
        db.AddRange(enrollment, lesson); await db.SaveChangesAsync();
        if (completed)
        {
            db.Add(new LessonProgress
            {
                TenantId = 101, CourseEnrollmentId = enrollment.Id, LessonId = lesson.Id,
                IsCompleted = true, ProgressPercent = 100m
            });
            await db.SaveChangesAsync();
        }
        return (course, student, enrollment, lesson);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-certificate-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LmsWorkflowService Service(EduOSDbContext db, string role = "TenantAdmin",
        long tenant = 101, long userId = 77) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Employee>(db), new GenericRepository<AcademicProgram>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<FileAsset>(db),
        db, new TestUser(role, tenant, userId), TimeProvider.System,
        NullLogger<LmsWorkflowService>.Instance,
        completionQuizzes: new GenericRepository<Quiz>(db),
        completionAttempts: new GenericRepository<QuizAttempt>(db),
        certificates: new GenericRepository<LmsCompletionCertificate>(db),
        certificateAudits: new GenericRepository<AuditLog>(db));

    private sealed class TestUser(string role, long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
