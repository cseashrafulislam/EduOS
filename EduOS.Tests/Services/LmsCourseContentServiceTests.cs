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
        db.AddRange(course, local); await db.SaveChangesAsync();
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

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-content-" + Guid.NewGuid().ToString("N")).Options;
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = context });
    }
    private static LmsCourseContentService Service(EduOSDbContext db, string role) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseSection>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonResource>(db),
        new GenericRepository<FileAsset>(db), new GenericRepository<Employee>(db),
        new GenericRepository<Student>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<AcademicBatch>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db), new GenericRepository<InstructorAssignment>(db), new GenericRepository<EmployeeCampusAssignment>(db),
        db, new TestUser(role), TimeProvider.System, NullLogger<LmsCourseContentService>.Instance);

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