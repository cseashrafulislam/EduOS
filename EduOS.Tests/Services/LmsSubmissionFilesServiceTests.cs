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

public sealed class LmsSubmissionFilesServiceTests
{
    [Fact]
    public async Task Multi_file_submission_is_atomic_and_exact_replay_is_idempotent()
    {
        await using var db = Database();
        var (assignment, enrollment) = await Seed(db);
        var a = File(101, 51, true); var b = File(101, 51, true);
        db.AddRange(a, b); await db.SaveChangesAsync();
        var service = Service(db);
        var request = new SubmitAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AssignmentReference = assignment.PublicId,
            CourseEnrollmentId = enrollment.Id, FileAssetReferences = [a.PublicId, b.PublicId]
        };
        var first = await service.SubmitAssignmentAsync(request);
        Assert.True(first.Success, first.Message);
        Assert.True((await service.SubmitAssignmentAsync(request)).Success);
        Assert.Equal(1, await db.Set<AssignmentSubmission>().CountAsync());
        Assert.Equal(2, await db.Set<AssignmentSubmissionFile>().CountAsync());
        var files = await service.GetSubmissionFilesAsync(first.Data!.Id);
        Assert.True(files.Success);
        Assert.Equal(2, files.Data!.Count);
        var workspace = await service.GetMyAssignmentWorkAsync(1, 20, null);
        Assert.True(workspace.Success);
        Assert.Single(workspace.Data!.Items);
        Assert.False(workspace.Data.Items[0].CanSubmit);
    }

    [Theory]
    [InlineData(101L, 99L, true)]
    [InlineData(101L, 51L, false)]
    [InlineData(202L, 51L, true)]
    public async Task Unsafe_foreign_or_unowned_file_is_rejected(long tenant, long uploader, bool safe)
    {
        await using var db = Database();
        var (assignment, enrollment) = await Seed(db);
        var file = File(tenant, uploader, safe);
        db.Add(file); await TestTenantSeeder.SaveAsync(db);
        var result = await Service(db).SubmitAssignmentAsync(new SubmitAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AssignmentReference = assignment.PublicId,
            CourseEnrollmentId = enrollment.Id, FileAssetReferences = [file.PublicId]
        });
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(await db.Set<AssignmentSubmission>().ToListAsync());
        Assert.Empty(await db.Set<AssignmentSubmissionFile>().ToListAsync());
    }

    [Fact]
    public async Task Student_cannot_read_another_students_submission_and_cannot_change_files_after_submit()
    {
        await using var db = Database();
        var (assignment, enrollment) = await Seed(db);
        var a = File(101, 51, true); var b = File(101, 51, true);
        db.AddRange(a, b); await db.SaveChangesAsync();
        var request = new SubmitAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AssignmentReference = assignment.PublicId,
            CourseEnrollmentId = enrollment.Id, FileAssetReferences = [a.PublicId]
        };
        var first = await Service(db).SubmitAssignmentAsync(request);
        Assert.True(first.Success, first.Message);
        Assert.Equal(403, (await Service(db, 99).GetSubmissionFilesAsync(first.Data!.Id)).StatusCode);
        request.FileAssetReferences = [b.PublicId];
        Assert.Equal(409, (await Service(db).SubmitAssignmentAsync(request)).StatusCode);
        Assert.Single(await db.Set<AssignmentSubmissionFile>().ToListAsync());
    }

    private static async Task<(Assignment, CourseEnrollment)> Seed(EduOSDbContext db)
    {
        var student = new Student { TenantId = 101, PersonId = 1, UserId = 51,
            StudentCode = "S1", FullName = "Learner", StatusCode = "Active" };
        var course = new Course { TenantId = 101, Code = "LMS1", Title = "Course", IsActive = true };
        db.AddRange(student, course); await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment { TenantId = 101, CourseId = course.Id,
            StudentId = student.Id, ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active };
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id,
            Title = "Task", IsPublished = true, MaxMarks = 100 };
        db.AddRange(enrollment, assignment); await db.SaveChangesAsync();
        return (assignment, enrollment);
    }
    private static FileAsset File(long tenant, long uploader, bool safe) => new()
    {
        TenantId = tenant, UploadedByUserId = uploader, IsVerifiedSafe = safe,
        StorageProvider = "local", StorageKey = Guid.NewGuid().ToString("N"),
        OriginalFileName = "work.pdf", ContentType = "application/pdf", SizeBytes = 123
    };
    private static EduOSDbContext Database()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-files-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static LmsWorkflowService Service(EduOSDbContext db, long userId = 51) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Employee>(db), new GenericRepository<AcademicProgram>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<FileAsset>(db),
        db, new StudentUser(userId), TimeProvider.System, NullLogger<LmsWorkflowService>.Instance,
        new GenericRepository<AssignmentSubmissionFile>(db));
    private sealed class StudentUser(long id) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => 101;
        public string? FullName => "Student";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Student"];
        public bool IsInRole(string role) => role == "Student";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
