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

public sealed class LmsSubmissionReviewTests
{
    [Fact]
    public async Task Manager_reviews_only_tenant_owned_submissions_with_student_names()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "REVIEW", Title = "Review" };
        var student = new Student { TenantId = 101, PersonId = 1, UserId = 51, StudentCode = "S-51", FullName = "Learner", StatusCode = "Active" };
        db.AddRange(course, student); await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment { TenantId = 101, CourseId = course.Id, StudentId = student.Id, ClientRequestId = Guid.NewGuid() };
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id, Title = "Essay", IsPublished = true, MaxMarks = 20 };
        db.AddRange(enrollment, assignment); await db.SaveChangesAsync();
        db.Add(new AssignmentSubmission { TenantId = 101, AssignmentId = assignment.Id, CourseEnrollmentId = enrollment.Id,
            SubmissionText = "Work", State = LearningSubmissionState.Submitted, RowVersion = [1, 2, 3] });
        await db.SaveChangesAsync();
        var response = await Service(db, "TenantAdmin").GetAssignmentSubmissionsAsync(assignment.PublicId);
        Assert.True(response.Success);
        Assert.Single(response.Data!.Items);
        Assert.Equal("Learner", response.Data.Items[0].StudentName);
        Assert.Equal("Work", response.Data.Items[0].SubmissionText);
        Assert.Equal(403, (await Service(db, "Student").GetAssignmentSubmissionsAsync(assignment.PublicId)).StatusCode);
        Assert.Equal(400, (await Service(db, "TenantAdmin").GetAssignmentSubmissionsAsync(assignment.PublicId, 0, 25)).StatusCode);
    }

    [Fact]
    public async Task Foreign_tenant_assignment_is_not_visible()
    {
        await using var db = Db();
        var course = new Course { TenantId = 202, Code = "PRIVATE", Title = "Private" };
        db.Add(course); await TestTenantSeeder.SaveAsync(db);
        var assignment = new Assignment { TenantId = 202, CourseId = course.Id, Title = "Hidden" };
        db.Add(assignment); await TestTenantSeeder.SaveAsync(db);
        Assert.Equal(404, (await Service(db, "TenantAdmin").GetAssignmentSubmissionsAsync(assignment.PublicId)).StatusCode);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-review-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LmsWorkflowService Service(EduOSDbContext db, string role) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Employee>(db), new GenericRepository<AcademicProgram>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<FileAsset>(db),
        db, new TestUser(role), TimeProvider.System, NullLogger<LmsWorkflowService>.Instance);

    private sealed class TestUser(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 51;
        public long TenantId => 101;
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
