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
    public async Task Assignment_rubric_requires_authorized_versioned_total_and_grades_are_atomic()
    {
        await using var db = Database();
        var (assignment, enrollment) = await Seed(db);
        assignment.RowVersion = [11, 12, 13, 14, 15, 16, 17, 18];
        await db.SaveChangesAsync();
        var version = Convert.ToBase64String(assignment.RowVersion);
        var criteria = new SaveAssignmentRubricCriterionDto[]
        {
            new() { Title = "Accuracy", MaxMarks = 60 },
            new() { Title = "Explanation", MaxMarks = 40 }
        };
        Assert.Equal(403, (await Service(db).SaveAssignmentRubricAsync(assignment.PublicId,
            new SaveAssignmentRubricRequestDto { RowVersion = version, Criteria = criteria })).StatusCode);
        var manager = Service(db, 77, role: "TenantAdmin");
        Assert.Equal(409, (await manager.SaveAssignmentRubricAsync(assignment.PublicId,
            new SaveAssignmentRubricRequestDto
            {
                RowVersion = version, Criteria = [new() { Title = "Accuracy", MaxMarks = 50 }]
            })).StatusCode);
        var saved = await manager.SaveAssignmentRubricAsync(assignment.PublicId,
            new SaveAssignmentRubricRequestDto { RowVersion = version, Criteria = criteria });
        Assert.True(saved.Success, saved.Message);
        Assert.Equal(2, saved.Data!.Criteria.Count);
        Assert.Equal(100, saved.Data.Criteria.Sum(x => x.MaxMarks));
        Assert.Equal(2, (await Service(db).GetAssignmentRubricAsync(assignment.PublicId)).Data!.Criteria.Count);
        Assert.Equal(404, (await Service(db, 99).GetAssignmentRubricAsync(assignment.PublicId)).StatusCode);
        var submitted = await Service(db).SubmitAssignmentAsync(new SubmitAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AssignmentReference = assignment.PublicId,
            CourseEnrollmentId = enrollment.Id, SubmissionText = "Answer with evidence"
        });
        Assert.True(submitted.Success, submitted.Message);
        Assert.Equal(409, (await manager.SaveAssignmentRubricAsync(assignment.PublicId,
            new SaveAssignmentRubricRequestDto { RowVersion = version, Criteria = criteria })).StatusCode);
        var grading = new GradeAssignmentSubmissionRequestDto
        {
            RowVersion = Convert.ToBase64String((await db.Set<AssignmentSubmission>().SingleAsync()).RowVersion),
            Marks = 90, Feedback = "Clear work",
            RubricScores =
            [
                new() { CriterionReference = saved.Data.Criteria[0].Reference, AwardedMarks = 55 },
                new() { CriterionReference = saved.Data.Criteria[1].Reference, AwardedMarks = 25 }
            ]
        };
        var submissionRow = await db.Set<AssignmentSubmission>().SingleAsync();
        submissionRow.RowVersion = [21, 22, 23, 24, 25, 26, 27, 28];
        await db.SaveChangesAsync();
        grading.RowVersion = Convert.ToBase64String(submissionRow.RowVersion);
        Assert.Equal(409, (await manager.GradeSubmissionAsync(submissionRow.Id, grading)).StatusCode);
        Assert.Empty(await db.Set<AssignmentRubricScore>().ToListAsync());
        grading.RubricScores[1].AwardedMarks = 35;
        var graded = await manager.GradeSubmissionAsync(submissionRow.Id, grading);
        Assert.True(graded.Success, graded.Message);
        Assert.Equal(90, graded.Data!.Marks);
        var ownScores = await Service(db).GetSubmissionRubricScoresAsync(submissionRow.Id);
        Assert.True(ownScores.Success, ownScores.Message);
        Assert.Equal(2, ownScores.Data!.Count);
        Assert.Equal(90, ownScores.Data.Sum(x => x.AwardedMarks));
        Assert.Equal(404, (await Service(db, 99).GetSubmissionRubricScoresAsync(submissionRow.Id)).StatusCode);
        Assert.Equal(409, (await manager.GradeSubmissionAsync(submissionRow.Id, grading)).StatusCode);
        Assert.Equal(2, await db.Set<AssignmentRubricScore>().CountAsync());
    }

    [Fact]
    public async Task Rubric_relations_have_tenant_scoped_foreign_keys_and_unique_score_receipts()
    {
        await using var db = Database();
        var model = db.Model;
        var criteria = model.FindEntityType(typeof(AssignmentRubricCriterion))!;
        var scores = model.FindEntityType(typeof(AssignmentRubricScore))!;
        Assert.Contains(criteria.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Assignment) &&
            fk.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "AssignmentId"]));
        Assert.Contains(scores.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(AssignmentSubmission) &&
            fk.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "AssignmentSubmissionId"]));
        Assert.Contains(scores.GetIndexes(), x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(
                ["TenantId", "AssignmentSubmissionId", "AssignmentRubricCriterionId"]));
    }

    [Fact]
    public async Task LMS_student_lookup_is_tenant_filtered_and_manager_only()
    {
        await using var db = Database();
        await Seed(db);
        db.AddRange(
            new Student { TenantId = 101, PersonId = 2, StudentCode = "ST-22",
                FullName = "Student Twenty Two", UserId = 77, StatusCode = "Active" },
            new Student { TenantId = 101, PersonId = 3, StudentCode = "ST-99",
                FullName = "Student Ninety Nine", UserId = 88, StatusCode = "Inactive" });
        await db.SaveChangesAsync();
        var manager = Service(db, 77, role: "TenantAdmin");
        var search = await manager.SearchEligibleStudentsAsync("ST-", 25);
        Assert.True(search.Success, search.Message);
        Assert.Single(search.Data!);
        Assert.Equal("ST-22", search.Data![0].StudentCode);
        Assert.Equal(403, (await Service(db).SearchEligibleStudentsAsync("ST-", 25)).StatusCode);
        Assert.Equal(400, (await manager.SearchEligibleStudentsAsync("S", 25)).StatusCode);
        Assert.Equal(400, (await manager.SearchEligibleStudentsAsync("ST-", 51)).StatusCode);
    }

    [Fact]
    public async Task Manager_enrollment_is_idempotent_and_checks_student_state_inside_transaction()
    {
        await using var db = Database();
        var (assignment, _) = await Seed(db);
        var course = await db.Set<Course>().SingleAsync(x => x.Id == assignment.CourseId);
        var student = new Student { TenantId = 101, PersonId = 4, UserId = 81,
            StudentCode = "ST-NEW", FullName = "New Learner", StatusCode = "Active" };
        db.Add(student);await db.SaveChangesAsync();
        var manager = Service(db, 77, role: "TenantAdmin");
        var input = new EnrollCourseRequestDto
        {
            ClientRequestId = Guid.NewGuid(), CourseReference = course.PublicId,
            StudentReference = student.PublicId
        };
        var first = await manager.EnrollStudentAsync(input);
        Assert.True(first.Success, first.Message);
        var replay = await manager.EnrollStudentAsync(input);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(first.Data!.Id, replay.Data!.Id);
        Assert.Equal(2, await db.Set<CourseEnrollment>().CountAsync());
        student.StatusCode = "Inactive";await db.SaveChangesAsync();
        var revoked = await manager.EnrollStudentAsync(new EnrollCourseRequestDto
        {
            ClientRequestId = Guid.NewGuid(), CourseReference = course.PublicId,
            StudentReference = student.PublicId
        });
        Assert.Equal(404, revoked.StatusCode);
    }
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
    public async Task Authorized_course_manager_can_read_submission_bytes_but_unrelated_user_cannot()
    {
        await using var db = Database();
        var (assignment, enrollment) = await Seed(db);
        var payload = new byte[] { 2, 4, 6 };
        var file = File(101, 51, true);
        file.SizeBytes = payload.Length;
        file.Sha256 = Convert.ToHexString(SHA256.HashData(payload));
        db.Add(file); await db.SaveChangesAsync();
        var submission = await Service(db).SubmitAssignmentAsync(new SubmitAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AssignmentReference = assignment.PublicId,
            CourseEnrollmentId = enrollment.Id, FileAssetReferences = [file.PublicId]
        });
        Assert.True(submission.Success, submission.Message);
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(file.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = payload });
        var manager = Service(db, 77, storage: storage.Object, role: "TenantAdmin");
        var allowed = await manager.DownloadSubmissionFileAsync(submission.Data!.Id, file.PublicId);
        Assert.True(allowed.Success, allowed.Message);
        Assert.Equal(payload, allowed.Data!.Content);
        Assert.Equal(404, (await Service(db, 77, storage: storage.Object)
            .DownloadSubmissionFileAsync(submission.Data.Id, file.PublicId)).StatusCode);
        storage.Verify(x => x.GetPrivateFileAsync(file.StorageKey), Times.Once);
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
        IFileUploadService? storage = null, IDocumentFileService? documents = null,
        string role = "Student") => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Employee>(db), new GenericRepository<AcademicProgram>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<FileAsset>(db),
        db, new StudentUser(userId, role), TimeProvider.System, NullLogger<LmsWorkflowService>.Instance,
        new GenericRepository<AssignmentSubmissionFile>(db),
        privateStorage: storage, documentFiles: documents,
        rubricCriteria: new GenericRepository<AssignmentRubricCriterion>(db),
        rubricScores: new GenericRepository<AssignmentRubricScore>(db));
    private sealed class StudentUser(long id, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => 101;
        public string? FullName => "Student";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
