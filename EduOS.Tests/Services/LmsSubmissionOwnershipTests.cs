using System.Security.Claims;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
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

public sealed class LmsSubmissionOwnershipTests
{
    [Theory]
    [InlineData(51L, true, true, true)]
    [InlineData(99L, true, true, false)]
    [InlineData(null, true, true, false)]
    [InlineData(51L, false, true, false)]
    [InlineData(51L, true, false, false)]
    public async Task Submission_requires_owned_safe_file_and_active_course(
        long? uploaderId, bool verifiedSafe, bool courseActive, bool shouldSucceed)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"lms-file-ownership-{Guid.NewGuid():N}").Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "51"),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim("TenantId", "101")
            ], "Test"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var student = new Student
        {
            TenantId = 101, PersonId = 1, UserId = 51, StudentCode = "S-001",
            FullName = "Student One", StatusCode = "Active"
        };
        var course = new Course
        {
            TenantId = 101, Code = "C-001", Title = "Course One", IsActive = courseActive
        };
        db.AddRange(student, course);
        await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment
        {
            TenantId = 101, CourseId = course.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active
        };
        var assignment = new Assignment
        {
            TenantId = 101, CourseId = course.Id, Title = "Homework",
            MaxMarks = 100m, IsPublished = true
        };
        var file = new FileAsset
        {
            TenantId = 101, UploadedByUserId = uploaderId, IsVerifiedSafe = verifiedSafe,
            StorageProvider = "local", StorageKey = "tenant-101/test.pdf",
            OriginalFileName = "homework.pdf", ContentType = "application/pdf", SizeBytes = 100
        };
        db.AddRange(enrollment, assignment, file);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var response = await service.SubmitAssignmentAsync(new SubmitAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AssignmentReference = assignment.PublicId,
            CourseEnrollmentId = enrollment.Id, FileAssetId = file.Id
        });
        Assert.Equal(shouldSucceed, response.Success);
        Assert.Equal(shouldSucceed ? 1 : 0, await db.AssignmentSubmissions.CountAsync());
        if (!shouldSucceed) Assert.Equal(409, response.StatusCode);
        else Assert.Equal(file.Id, response.Data!.FileAssetId);
    }

    private static LmsWorkflowService CreateService(EduOSDbContext db) => new(
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
        db, new StudentUser(), TimeProvider.System,
        NullLogger<LmsWorkflowService>.Instance);

    private sealed class StudentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 51;
        public long TenantId => 101;
        public string? FullName => "Student One";
        public string? Email => "student@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Student"];
        public bool IsInRole(string role) => role == "Student";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "LMS security test";
    }
}
