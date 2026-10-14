using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LmsAssignmentAttachmentServiceTests
{
    [Fact]
    public async Task Safe_file_link_is_idempotent_and_unsafe_file_rejected()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "COURSE", Title = "Course" };
        db.Add(course); await db.SaveChangesAsync();
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id, Title = "Homework" };
        var file = File(101, true); var unsafeFile = File(101, false);
        db.AddRange(assignment, file, unsafeFile); await db.SaveChangesAsync();
        var service = Service(db, "TenantAdmin");
        var request = new SaveAssignmentAttachmentRequestDto
        { AssignmentReference = assignment.PublicId, FileAssetReference = file.PublicId, Title = "Guide" };
        var first = await service.SaveAssignmentAttachmentAsync(null, request);
        Assert.True(first.Success, first.Message);
        Assert.Equal(first.Data!.Id, (await service.SaveAssignmentAttachmentAsync(null, request)).Data!.Id);
        Assert.Single((await service.GetAssignmentAttachmentsAsync(assignment.PublicId)).Data!);
        Assert.Equal(409, (await service.SaveAssignmentAttachmentAsync(null, new SaveAssignmentAttachmentRequestDto
        { AssignmentReference = assignment.PublicId, FileAssetReference = unsafeFile.PublicId })).StatusCode);
        Assert.Equal(1, await db.Set<AssignmentAttachment>().CountAsync());
    }

    [Fact]
    public async Task Student_cannot_author_and_draft_attachment_is_hidden()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "HIDDEN", Title = "Hidden" };
        var student = new Student { TenantId = 101, StudentCode = "ST1", FullName = "Student",
            UserId = 51, StatusCode = "Active" };
        db.AddRange(course, student); await db.SaveChangesAsync();
        db.Add(new CourseEnrollment { TenantId = 101, CourseId = course.Id, StudentId = student.Id });
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id, Title = "Draft" };
        var file = File(101, true);
        db.AddRange(assignment, file); await db.SaveChangesAsync();
        var service = Service(db, "Student");
        Assert.Equal(403, (await service.SaveAssignmentAttachmentAsync(null, new SaveAssignmentAttachmentRequestDto
        { AssignmentReference = assignment.PublicId, FileAssetReference = file.PublicId })).StatusCode);
        Assert.Equal(404, (await service.GetAssignmentAttachmentsAsync(assignment.PublicId)).StatusCode);
        assignment.IsPublished = true; await db.SaveChangesAsync();
        Assert.True((await service.GetAssignmentAttachmentsAsync(assignment.PublicId)).Success);
    }

    [Fact]
    public async Task Foreign_tenant_file_and_stale_version_are_rejected()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "ISOLATION", Title = "Isolation" };
        db.Add(course); await db.SaveChangesAsync();
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id, Title = "Homework" };
        var foreign = File(202, true); var local = File(101, true);
        db.AddRange(assignment, foreign, local); await db.SaveChangesAsync();
        var service = Service(db, "TenantAdmin");
        Assert.Equal(409, (await service.SaveAssignmentAttachmentAsync(null, new SaveAssignmentAttachmentRequestDto
        { AssignmentReference = assignment.PublicId, FileAssetReference = foreign.PublicId })).StatusCode);
        var row = new AssignmentAttachment { TenantId = 101, AssignmentId = assignment.Id,
            FileAssetId = local.Id, RowVersion = new byte[] { 1, 2, 3 } };
        db.Add(row); await db.SaveChangesAsync();
        Assert.Equal(409, (await service.SaveAssignmentAttachmentAsync(row.Id, new SaveAssignmentAttachmentRequestDto
        { AssignmentReference = assignment.PublicId, FileAssetReference = local.PublicId,
          RowVersion = Convert.ToBase64String(new byte[] { 4, 5, 6 }) })).StatusCode);
        var version = Convert.ToBase64String(row.RowVersion);
        Assert.True((await service.RemoveAssignmentAttachmentAsync(row.Id, version)).Success);
        Assert.True((await service.RemoveAssignmentAttachmentAsync(row.Id, version)).Success);
    }

    private static FileAsset File(long tenant, bool safe) => new()
    {
        TenantId = tenant, StorageProvider = "Local", StorageKey = "private/file",
        OriginalFileName = "guide.pdf", ContentType = "application/pdf",
        IsVerifiedSafe = safe, UploadedByUserId = 51
    };
    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-assignment-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static LmsCourseContentService Service(EduOSDbContext db, string role) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseSection>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonResource>(db),
        new GenericRepository<FileAsset>(db), new GenericRepository<Employee>(db),
        new GenericRepository<Student>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<AcademicBatch>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db), new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<EmployeeCampusAssignment>(db), db, new TestUser(role),
        TimeProvider.System, NullLogger<LmsCourseContentService>.Instance,
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentAttachment>(db));

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
