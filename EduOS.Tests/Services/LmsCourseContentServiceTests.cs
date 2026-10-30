using EduOS.Core.DTOs.LMS;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Common;
using EduOS.Service.Helpers.Storage;
using Moq;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
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

public sealed class LmsCourseContentServiceTests
{
    [Fact]
    public async Task Section_create_is_idempotent_and_duplicate_order_is_rejected()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "BIO", Title = "Biology", IsActive = true };
        db.Add(course); await db.SaveChangesAsync();
        var service = Service(db, "TenantAdmin");
        var request = new SaveCourseSectionRequestDto
        { CourseReference = course.PublicId, Title = "Introduction", DisplayOrder = 1, IsPublished = true };
        var first = await service.SaveSectionAsync(null, request);
        Assert.True(first.Success, first.Message);
        var retry = await service.SaveSectionAsync(null, request);
        Assert.True(retry.Success, retry.Message);
        Assert.Equal(first.Data!.Id, retry.Data!.Id);
        Assert.Equal(409, (await service.SaveSectionAsync(null, new SaveCourseSectionRequestDto
        { CourseReference = course.PublicId, Title = "Another", DisplayOrder = 1 })).StatusCode);
        var view = await service.GetCourseContentAsync(course.PublicId);
        Assert.True(view.Success, view.Message);
        Assert.Single(view.Data!.Sections);
    }

    [Fact]
    public async Task Cross_tenant_reference_and_unenrolled_student_are_hidden()
    {
        await using var db = Db();
        var course = new Course { TenantId = 202, Code = "PRIVATE", Title = "Other tenant" };
        var local = new Course { TenantId = 101, Code = "LOCAL", Title = "Local" };
        db.AddRange(course, local); await TestTenantSeeder.SaveAsync(db);
        Assert.Equal(404, (await Service(db, "TenantAdmin").GetCourseContentAsync(course.PublicId)).StatusCode);
        Assert.Equal(403, (await Service(db, "Student").SaveSectionAsync(null,
            new SaveCourseSectionRequestDto { CourseReference = local.PublicId, Title = "Unauthorized" })).StatusCode);
        Assert.Equal(404, (await Service(db, "Student").GetCourseContentAsync(local.PublicId)).StatusCode);
    }

    [Fact]
    public async Task Enrolled_student_sees_only_published_sections_and_lessons()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "MATH", Title = "Mathematics" };
        var student = new Student { TenantId = 101, FullName = "Student", StudentCode = "S-1", UserId = 51, StatusCode = "Active" };
        db.AddRange(course, student); await db.SaveChangesAsync();
        db.Add(new CourseEnrollment { TenantId = 101, CourseId = course.Id, StudentId = student.Id });
        var draft = new CourseSection { TenantId = 101, CourseId = course.Id, Title = "Draft", DisplayOrder = 1 };
        var published = new CourseSection { TenantId = 101, CourseId = course.Id, Title = "Published", DisplayOrder = 2, IsPublished = true };
        db.AddRange(draft, published); await db.SaveChangesAsync();
        db.AddRange(new Lesson { TenantId = 101, CourseId = course.Id, CourseSectionId = draft.Id,
            Title = "Hidden", IsPublished = true },
            new Lesson { TenantId = 101, CourseId = course.Id, CourseSectionId = published.Id,
            Title = "Visible", IsPublished = true },
            new Lesson { TenantId = 101, CourseId = course.Id, CourseSectionId = published.Id,
            Title = "Unpublished", IsPublished = false });
        await db.SaveChangesAsync();
        var result = await Service(db, "Student").GetCourseContentAsync(course.PublicId);
        Assert.True(result.Success, result.Message);
        Assert.Single(result.Data!.Sections);
        Assert.Single(result.Data.Sections[0].Lessons);
        Assert.Equal("Visible", result.Data.Sections[0].Lessons[0].Title);
        Assert.False(result.Data.CanEdit);
    }

    [Fact]
    public async Task Resource_requires_safe_file_or_https_and_exact_retry_is_idempotent()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "ENG", Title = "English" };
        db.Add(course); await db.SaveChangesAsync();
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Grammar" };
        var file = new FileAsset { TenantId = 101, StorageProvider = "Local", StorageKey = "private/test",
            OriginalFileName = "notes.pdf", ContentType = "application/pdf", IsVerifiedSafe = false };
        db.AddRange(lesson, file); await db.SaveChangesAsync();
        var service = Service(db, "TenantAdmin");
        Assert.Equal(400, (await service.SaveResourceAsync(null, new SaveLessonResourceRequestDto
        { LessonReference = lesson.PublicId, ExternalUrl = "http://insecure.example/file" })).StatusCode);
        Assert.Equal(409, (await service.SaveResourceAsync(null, new SaveLessonResourceRequestDto
        { LessonReference = lesson.PublicId, FileAssetReference = file.PublicId })).StatusCode);
        var request = new SaveLessonResourceRequestDto
        { LessonReference = lesson.PublicId, Title = "Handout", ExternalUrl = "https://example.com/handout", DisplayOrder = 1 };
        var first = await service.SaveResourceAsync(null, request);
        Assert.True(first.Success, first.Message);
        var retry = await service.SaveResourceAsync(null, request);
        Assert.True(retry.Success, retry.Message);
        Assert.Equal(first.Data!.Id, retry.Data!.Id);
        Assert.Equal(409, (await service.SaveResourceAsync(null, new SaveLessonResourceRequestDto
        { LessonReference = lesson.PublicId, Title = "Other", ExternalUrl = "https://example.com/other", DisplayOrder = 1 })).StatusCode);
    }

    [Fact]
    public async Task Published_lesson_cannot_move_into_draft_section()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "SCI", Title = "Science" };
        db.Add(course); await db.SaveChangesAsync();
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Atoms", IsPublished = true };
        var section = new CourseSection { TenantId = 101, CourseId = course.Id, Title = "Draft", DisplayOrder = 1 };
        db.AddRange(lesson, section); await db.SaveChangesAsync();
        var response = await Service(db, "TenantAdmin").AssignLessonSectionAsync(new AssignLessonSectionRequestDto
        { CourseReference = course.PublicId, LessonReference = lesson.PublicId, CourseSectionId = section.Id, RowVersion = "AQID" });
        Assert.Equal(409, response.StatusCode);
    }

    [Fact]
    public async Task Published_academic_section_rejects_inactive_term()
    {
        await using var db = Db();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka")));
        var course = new Course { TenantId = 101, Code = "TERM", Title = "Term course",
            AcademicProgramId = 10, SubjectId = 20 };
        var year = new AcademicYear { TenantId = 101, Name = "Year", Code = "YR",
            StartDate = today.AddDays(-30), EndDate = today.AddDays(30) };
        var curriculum = new AcademicCurriculum { TenantId = 101, AcademicProgramId = 10,
            Code = "CUR", Name = "Curriculum", EffectiveFrom = today.AddDays(-30) };
        db.AddRange(course, year, curriculum); await db.SaveChangesAsync();
        var term = new AcademicTerm { TenantId = 101, AcademicYearId = year.Id, Name = "Term",
            StartDate = today.AddDays(-10), EndDate = today.AddDays(10), IsActive = false };
        var subject = new CurriculumSubject { TenantId = 101, AcademicCurriculumId = curriculum.Id,
            AcademicLevelId = 30, SubjectId = 20 };
        db.AddRange(term, subject); await db.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = 101, CampusId = 40, AcademicYearId = year.Id,
            AcademicTermId = term.Id, AcademicProgramId = 10, AcademicLevelId = 30,
            Code = "B1", Name = "Batch" };
        db.Add(batch); await db.SaveChangesAsync();
        db.Add(new SubjectOffering { TenantId = 101, AcademicBatchId = batch.Id,
            CurriculumSubjectId = subject.Id, AcademicYearId = year.Id,
            AcademicTermId = term.Id, Code = "O1" }); await db.SaveChangesAsync();
        var service = Service(db, "TenantAdmin");
        var request = new SaveCourseSectionRequestDto { CourseReference = course.PublicId,
            Title = "Published", DisplayOrder = 1, IsPublished = true };
        Assert.Equal(409, (await service.SaveSectionAsync(null, request)).StatusCode);
        term.IsActive = true; await db.SaveChangesAsync();
        Assert.True((await service.SaveSectionAsync(null, request)).Success);
    }

    [Fact]
    public async Task Completed_enrollment_blocks_section_and_resource_authoring()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "LOCKED", Title = "Locked" };
        db.Add(course); await db.SaveChangesAsync();
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Lesson" };
        var enrollment = new CourseEnrollment { TenantId = 101, CourseId = course.Id,
            StudentId = 123, State = CourseEnrollmentState.Completed, CompletedAt = DateTime.UtcNow };
        db.AddRange(lesson, enrollment); await db.SaveChangesAsync();
        var service = Service(db, "TenantAdmin");
        Assert.Equal(409, (await service.SaveSectionAsync(null, new SaveCourseSectionRequestDto
        { CourseReference = course.PublicId, Title = "New section", DisplayOrder = 1 })).StatusCode);
        Assert.Equal(409, (await service.SaveResourceAsync(null, new SaveLessonResourceRequestDto
        { LessonReference = lesson.PublicId, Title = "Resource", DisplayOrder = 1,
          ExternalUrl = "https://example.com/lesson" })).StatusCode);
        Assert.Empty(await db.Set<CourseSection>().ToListAsync());
        Assert.Empty(await db.Set<LessonResource>().ToListAsync());
    }

    [Fact]
    public async Task Completed_enrollment_blocks_lesson_move_and_resource_removal()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "LOCK-MOVE", Title = "Locked move" };
        db.Add(course); await db.SaveChangesAsync();
        var section = new CourseSection { TenantId = 101, CourseId = course.Id,
            Title = "Published section", DisplayOrder = 1, IsPublished = true };
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id,
            Title = "Published lesson", IsPublished = true, RowVersion = [1, 2, 3] };
        db.AddRange(section, lesson); await db.SaveChangesAsync();
        var resource = new LessonResource { TenantId = 101, LessonId = lesson.Id,
            Title = "Handout", ExternalUrl = "https://example.com/handout", RowVersion = [1, 2, 3] };
        var completed = new CourseEnrollment { TenantId = 101, CourseId = course.Id,
            StudentId = 123, State = CourseEnrollmentState.Completed, CompletedAt = DateTime.UtcNow };
        db.AddRange(resource, completed); await db.SaveChangesAsync();
        var service = Service(db, "TenantAdmin");
        Assert.Equal(409, (await service.AssignLessonSectionAsync(new AssignLessonSectionRequestDto
        {
            CourseReference = course.PublicId, LessonReference = lesson.PublicId,
            CourseSectionId = section.Id, RowVersion = Convert.ToBase64String(lesson.RowVersion)
        })).StatusCode);
        Assert.Equal(409, (await service.RemoveResourceAsync(resource.Id,
            Convert.ToBase64String(resource.RowVersion))).StatusCode);
        Assert.Null(lesson.CourseSectionId);
        Assert.False(resource.IsDeleted);
    }


    [Fact]
    public async Task Private_lesson_download_checks_enrollment_publication_namespace_and_file_hash()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "SECURE", Title = "Secure resources" };
        var student = new Student { TenantId = 101, FullName = "Learner", StudentCode = "S-SEC", UserId = 51, StatusCode = "Active" };
        db.AddRange(course, student); await db.SaveChangesAsync();
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Published", IsPublished = true };
        var bytes = System.Text.Encoding.UTF8.GetBytes("verified LMS handout bytes");
        var file = new FileAsset
        {
            TenantId = 101, StorageProvider = "LocalPrivate", StorageKey = "tenant-101/lms/handout.pdf",
            OriginalFileName = "handout.pdf", ContentType = "application/pdf",
            SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            UploadedByUserId = 51, MalwareScanStateCode = "Clean",
            Visibility = FileVisibility.Private, IsVerifiedSafe = true
        };
        db.AddRange(lesson, file);
        db.Add(new CourseEnrollment { TenantId = 101, CourseId = course.Id, StudentId = student.Id });
        await db.SaveChangesAsync();
        var resource = new LessonResource { TenantId = 101, LessonId = lesson.Id, FileAssetId = file.Id, Title = "Handout" };
        db.Add(resource); await db.SaveChangesAsync();
        var storage = new Moq.Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(file.StorageKey)).ReturnsAsync(new FileDownloadResult { Content = bytes });
        var learner = Service(db, "Student", storage.Object);
        var allowed = await learner.DownloadResourceFileAsync(resource.Id);
        Assert.True(allowed.Success, allowed.Message);
        Assert.Equal(bytes, allowed.Data!.Content);
        Assert.Equal("handout.pdf", allowed.Data.FileName);
        lesson.IsPublished = false; await db.SaveChangesAsync();
        Assert.Equal(404, (await learner.DownloadResourceFileAsync(resource.Id)).StatusCode);
        lesson.IsPublished = true; file.StorageKey = "tenant-101/documents/private.pdf";
        await db.SaveChangesAsync();
        Assert.Equal(404, (await learner.DownloadResourceFileAsync(resource.Id)).StatusCode);
        file.StorageKey = "tenant-101/lms/handout.pdf"; file.Sha256 = new string('A', 64);
        await db.SaveChangesAsync();
        Assert.Equal(409, (await learner.DownloadResourceFileAsync(resource.Id)).StatusCode);
    }

    [Fact]
    public async Task Private_lesson_linking_rejects_other_users_and_document_assets()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "OWN", Title = "Ownership" };
        db.Add(course); await db.SaveChangesAsync();
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Lesson" };
        var file = new FileAsset
        {
            TenantId = 101, StorageProvider = "LocalPrivate", StorageKey = "tenant-101/lms/notes.pdf",
            OriginalFileName = "notes.pdf", ContentType = "application/pdf",
            SizeBytes = 2, Sha256 = new string('A', 64), UploadedByUserId = 999,
            MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private, IsVerifiedSafe = true
        };
        db.AddRange(lesson, file); await db.SaveChangesAsync();
        var admin = Service(db, "TenantAdmin");
        var request = new SaveLessonResourceRequestDto
        { LessonReference = lesson.PublicId, FileAssetReference = file.PublicId, DisplayOrder = 1 };
        Assert.Equal(409, (await admin.SaveResourceAsync(null, request)).StatusCode);
        file.UploadedByUserId = 51; file.StorageKey = "tenant-101/documents/notes.pdf";
        await db.SaveChangesAsync();
        Assert.Equal(409, (await admin.SaveResourceAsync(null, request)).StatusCode);
        file.StorageKey = "tenant-101/lms/notes.pdf"; await db.SaveChangesAsync();
        Assert.True((await admin.SaveResourceAsync(null, request)).Success);
    }


    [Fact]
    public async Task Resource_upload_requires_authorized_lesson_and_never_invokes_storage_for_students()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "UPLOAD", Title = "Upload access" };
        db.Add(course); await db.SaveChangesAsync();
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Author lesson" };
        db.Add(lesson); await db.SaveChangesAsync();
        var documents = new Mock<EduOS.Core.Interfaces.IServices.IDocumentFileService>();
        var file = new PrivateFileUploadDto
        {
            FileName = "notes.pdf", ContentType = "application/pdf", Length = 2,
            Content = new MemoryStream([1, 2])
        };
        var id = Guid.NewGuid();
        var denied = await Service(db, "Student", documents: documents.Object)
            .UploadResourceFileAsync(lesson.PublicId, id, file);
        Assert.Equal(403, denied.StatusCode);
        documents.Verify(x => x.UploadForLmsAsync(It.IsAny<Guid>(), It.IsAny<PrivateFileUploadDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
        documents.Setup(x => x.UploadForLmsAsync(id, file, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<FileAssetDto>.SuccessResponse(new FileAssetDto { Reference = id }));
        var allowed = await Service(db, "TenantAdmin", documents: documents.Object)
            .UploadResourceFileAsync(lesson.PublicId, id, file);
        Assert.True(allowed.Success, allowed.Message);
        Assert.Equal(id, allowed.Data!.Reference);
        course.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(404, (await Service(db, "TenantAdmin", documents: documents.Object)
            .UploadResourceFileAsync(lesson.PublicId, Guid.NewGuid(), file)).StatusCode);
        documents.Verify(x => x.UploadForLmsAsync(id, file, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-content-" + Guid.NewGuid().ToString("N")).Options;
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = context });
    }
    [Fact]
    public async Task Lesson_resource_revisions_are_author_scoped_and_baselined()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "HISTORY", Title = "History", IsActive = true };
        db.Add(course); await db.SaveChangesAsync();
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Original lesson", IsPublished = true };
        db.Add(lesson); await db.SaveChangesAsync();
        var author = Service(db, "TenantAdmin");
        var first = await author.SaveResourceAsync(null, new SaveLessonResourceRequestDto
        {
            LessonReference = lesson.PublicId, Title = "Handout",
            ExternalUrl = "https://example.org/first", DisplayOrder = 1
        });
        Assert.True(first.Success, first.Message);
        // SQLite fixtures do not generate SQL Server rowversions. A second independent
        // resource create still proves sequential immutable snapshots without faking a token.
        var second = await author.SaveResourceAsync(null, new SaveLessonResourceRequestDto
        {
            LessonReference = lesson.PublicId, Title = "Second handout",
            ExternalUrl = "https://example.org/second", DisplayOrder = 2
        });
        Assert.True(second.Success, second.Message);
        var history = await author.GetLessonRevisionsAsync(lesson.PublicId, 1, 20);
        Assert.True(history.Success, history.Message);
        Assert.Equal(3, history.Data!.TotalCount);
        Assert.Equal(new[] { 3, 2, 1 }, history.Data.Items.Select(x => x.RevisionNo));
        Assert.Equal("Original lesson", history.Data.Items[2].Title);
        Assert.Empty(history.Data.Items[2].Resources);
        Assert.Equal(2, history.Data.Items[0].Resources.Count);
        Assert.Equal("Second handout", history.Data.Items[0].Resources[1].Title);
        Assert.Equal(403, (await Service(db, "Student").GetLessonRevisionsAsync(lesson.PublicId, 1, 20)).StatusCode);
        Assert.Equal(404, (await author.GetLessonRevisionsAsync(Guid.NewGuid(), 1, 20)).StatusCode);
        Assert.Equal(400, (await author.GetLessonRevisionsAsync(lesson.PublicId, 1, 101)).StatusCode);
    }

    private static LmsCourseContentService Service(EduOSDbContext db, string role, IFileUploadService? storage = null, EduOS.Core.Interfaces.IServices.IDocumentFileService? documents = null) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseSection>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonResource>(db),
        new GenericRepository<FileAsset>(db), new GenericRepository<Employee>(db),
        new GenericRepository<Student>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<AcademicBatch>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db), new GenericRepository<InstructorAssignment>(db), new GenericRepository<EmployeeCampusAssignment>(db),
        db, new TestUser(role), TimeProvider.System, NullLogger<LmsCourseContentService>.Instance, storage: storage, documentFiles: documents,
        revisions: new GenericRepository<LmsLessonRevision>(db));

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
