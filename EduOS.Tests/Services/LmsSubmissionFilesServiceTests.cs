using EduOS.Core.DTOs.LMS;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Common;
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

public sealed class LmsSubmissionFilesServiceTests
{
    [Fact]
    public async Task Student_uploads_scanned_submission_file_only_for_owned_open_assignment()
    {
        await using var db = Database();
        var (assignment, enrollment) = await Seed(db);
        var documents = new Mock<IDocumentFileService>();
        var requestId = Guid.NewGuid();
        var reference = Guid.NewGuid();
        documents.Setup(x => x.UploadPortalAsync(It.IsAny<Guid>(), requestId,
                It.IsAny<PrivateFileUploadDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<FileAssetDto>.SuccessResponse(new FileAssetDto { Reference = reference }));
        var upload = new PrivateFileUploadDto
        {
            FileName = "work.pdf", ContentType = "application/pdf", Length = 4,
            Content = new MemoryStream([1, 2, 3, 4])
        };
        var result = await Service(db, documents: documents.Object)
            .UploadSubmissionFileAsync(assignment.PublicId, enrollment.Id, requestId, upload);
        Assert.True(result.Success, result.Message);
        Assert.Equal(reference, result.Data!.Reference);
        var own = await db.Students.SingleAsync(x => x.UserId == 51);
        documents.Verify(x => x.UploadPortalAsync(own.PublicId, requestId,
            It.IsAny<PrivateFileUploadDto>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(404, (await Service(db, 99, documents: documents.Object)
            .UploadSubmissionFileAsync(assignment.PublicId, enrollment.Id, Guid.NewGuid(), upload)).StatusCode);
        assignment.IsPublished = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await Service(db, documents: documents.Object)
            .UploadSubmissionFileAsync(assignment.PublicId, enrollment.Id, Guid.NewGuid(), upload)).StatusCode);
        documents.Verify(x => x.UploadPortalAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<PrivateFileUploadDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submission_byte_download_requires_scanned_owned_link_and_integrity()
    {
        await using var db = Database();
        var (assignment, enrollment) = await Seed(db);
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var storageKey = "tenant-101/documents/assignment.pdf";
        var file = new FileAsset
        {
            TenantId = 101, PublicId = Guid.NewGuid(), UploadedByUserId = 51,
            StorageProvider = "LocalPrivate", StorageKey = storageKey,
            OriginalFileName = "assignment.pdf", ContentType = "application/pdf",
            SizeBytes = payload.Length, Sha256 = Convert.ToHexString(SHA256.HashData(payload)),
            IsVerifiedSafe = true, MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private
        };
        db.Add(file);await db.SaveChangesAsync();
        var submitted = await Service(db).SubmitAssignmentAsync(new SubmitAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AssignmentReference = assignment.PublicId,
            CourseEnrollmentId = enrollment.Id, FileAssetReferences = [file.PublicId]
        });
        Assert.True(submitted.Success, submitted.Message);
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(storageKey))
            .ReturnsAsync(new FileDownloadResult { Content = payload });
        var service = Service(db, storage: storage.Object);
        var downloaded = await service.DownloadSubmissionFileAsync(submitted.Data!.Id, file.PublicId);
        Assert.True(downloaded.Success, downloaded.Message);
        Assert.Equal(payload, downloaded.Data!.Content);
        Assert.Equal("assignment.pdf", downloaded.Data.FileName);
        Assert.Equal(404, (await Service(db, 99, storage: storage.Object)
            .DownloadSubmissionFileAsync(submitted.Data.Id, file.PublicId)).StatusCode);
        Assert.Equal(404, (await service.DownloadSubmissionFileAsync(submitted.Data.Id, Guid.NewGuid())).StatusCode);
        storage.Verify(x => x.GetPrivateFileAsync(storageKey), Times.Once);
        storage.Setup(x => x.GetPrivateFileAsync(storageKey))
            .ReturnsAsync(new FileDownloadResult { Content = [9, 9, 9, 9, 9] });
        Assert.Equal(409, (await service.DownloadSubmissionFileAsync(submitted.Data.Id, file.PublicId)).StatusCode);
        file.MalwareScanStateCode = "Pending";
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.DownloadSubmissionFileAsync(submitted.Data.Id, file.PublicId)).StatusCode);
        storage.Verify(x => x.GetPrivateFileAsync(storageKey), Times.Exactly(2));
    }
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

    [Theory]
    [InlineData("tenant-101/documents/../escape.pdf", "Clean")]
    [InlineData("tenant-202/documents/foreign.pdf", "Clean")]
    [InlineData("tenant-101/documents/clean.pdf", "Pending")]
    [InlineData("tenant-101/public/leak.pdf", "Clean")]
    public async Task Submission_rejects_unscanned_or_invalid_private_storage_namespace(
        string key, string scanState)
    {
        await using var db = Database();
        var (assignment, enrollment) = await Seed(db);
        var file = File(101, 51, true);
        file.StorageKey = key;
        file.MalwareScanStateCode = scanState;
        db.Add(file); await db.SaveChangesAsync();
        var result = await Service(db).SubmitAssignmentAsync(new SubmitAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AssignmentReference = assignment.PublicId,
            CourseEnrollmentId = enrollment.Id, FileAssetReferences = [file.PublicId]
        });
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(await db.Set<AssignmentSubmission>().ToListAsync());
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
        MalwareScanStateCode = safe ? "Clean" : "Pending", Visibility = FileVisibility.Private,
        StorageProvider = "LocalPrivate", StorageKey = $"tenant-{tenant}/documents/{Guid.NewGuid():N}",
        Sha256 = new string('a', 64), OriginalFileName = "work.pdf",
        ContentType = "application/pdf", SizeBytes = 123
    };
    private static EduOSDbContext Database()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-files-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static LmsWorkflowService Service(EduOSDbContext db, long userId = 51,
        IFileUploadService? storage = null, IDocumentFileService? documents = null) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Employee>(db), new GenericRepository<AcademicProgram>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<FileAsset>(db),
        db, new StudentUser(userId), TimeProvider.System, NullLogger<LmsWorkflowService>.Instance,
        new GenericRepository<AssignmentSubmissionFile>(db),
        privateStorage: storage, documentFiles: documents);
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
