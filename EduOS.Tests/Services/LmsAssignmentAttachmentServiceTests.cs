using EduOS.Core.DTOs.LMS;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Common;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Helpers.Storage;
using Moq;
using System.Security.Cryptography;
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
    public async Task Author_stages_scanned_LMS_handout_for_owned_active_course_only()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "MATERIAL", Title = "Materials", IsActive = true };
        db.Add(course); await db.SaveChangesAsync();
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id, Title = "Guide" };
        db.Add(assignment); await db.SaveChangesAsync();
        var documents = new Mock<IDocumentFileService>();
        var requestId = Guid.NewGuid();
        documents.Setup(x => x.UploadForLmsAsync(requestId, It.IsAny<PrivateFileUploadDto>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(ApiResponse<FileAssetDto>.SuccessResponse(
            new FileAssetDto { Reference = Guid.NewGuid() }));
        var upload = new PrivateFileUploadDto { FileName = "guide.pdf", ContentType = "application/pdf",
            Length = 4, Content = new MemoryStream([1, 2, 3, 4]) };
        var author = Service(db, "TenantAdmin", documents: documents.Object);
        Assert.True((await author.UploadAssignmentAttachmentFileAsync(
            assignment.PublicId, requestId, upload)).Success);
        Assert.Equal(403, (await Service(db, "Student", documents: documents.Object)
            .UploadAssignmentAttachmentFileAsync(assignment.PublicId, Guid.NewGuid(), upload)).StatusCode);
        course.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(404, (await author.UploadAssignmentAttachmentFileAsync(
            assignment.PublicId, Guid.NewGuid(), upload)).StatusCode);
        documents.Verify(x => x.UploadForLmsAsync(It.IsAny<Guid>(), It.IsAny<PrivateFileUploadDto>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assignment_handout_download_checks_link_course_scope_scan_and_SHA256()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "FILES", Title = "Files", IsActive = true };
        db.Add(course); await db.SaveChangesAsync();
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id,
            Title = "Files", IsPublished = true };
        var file = File(101, true);
        var bytes = new byte[] { 1, 3, 5, 7 };
        file.SizeBytes = bytes.Length;
        file.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        db.AddRange(assignment, file); await db.SaveChangesAsync();
        var link = new AssignmentAttachment { TenantId = 101, AssignmentId = assignment.Id,
            FileAssetId = file.Id };
        db.Add(link); await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(file.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = bytes });
        var service = Service(db, "TenantAdmin", storage: storage.Object);
        var download = await service.DownloadAssignmentAttachmentAsync(link.Id);
        Assert.True(download.Success, download.Message);
        Assert.Equal(bytes, download.Data!.Content);
        Assert.True((await service.GetAssignmentAttachmentsAsync(assignment.PublicId))
            .Data!.Single().FileDownloadable);
        Assert.Equal(404, (await service.DownloadAssignmentAttachmentAsync(link.Id + 100)).StatusCode);
        storage.Setup(x => x.GetPrivateFileAsync(file.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = [9, 9, 9, 9] });
        Assert.Equal(409, (await service.DownloadAssignmentAttachmentAsync(link.Id)).StatusCode);
        file.MalwareScanStateCode = "Pending"; await db.SaveChangesAsync();
        Assert.Equal(404, (await service.DownloadAssignmentAttachmentAsync(link.Id)).StatusCode);
        storage.Verify(x => x.GetPrivateFileAsync(file.StorageKey), Times.Exactly(2));
    }

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
        db.AddRange(assignment, foreign, local); await TestTenantSeeder.SaveAsync(db);
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
        var removed = await service.RemoveAssignmentAttachmentAsync(row.Id, version);
        Assert.True(removed.Success, removed.Message);
        // Soft-deleted rows are hidden by the tenant query filter on later requests.
        Assert.Equal(404, (await service.RemoveAssignmentAttachmentAsync(row.Id, version)).StatusCode);
    }

    private static FileAsset File(long tenant, bool safe) => new()
    {
        TenantId = tenant, StorageProvider = "LocalPrivate",
        StorageKey = $"tenant-{tenant}/lms/{Guid.NewGuid():N}",
        OriginalFileName = "guide.pdf", ContentType = "application/pdf",
        IsVerifiedSafe = safe, MalwareScanStateCode = safe ? "Clean" : "Pending",
        Visibility = FileVisibility.Private, SizeBytes = 4, Sha256 = new string('a', 64),
        UploadedByUserId = 51
    };
    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-assignment-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static LmsCourseContentService Service(EduOSDbContext db, string role,
        IFileUploadService? storage = null, IDocumentFileService? documents = null) => new(
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
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentAttachment>(db),
        storage: storage, documentFiles: documents);

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
