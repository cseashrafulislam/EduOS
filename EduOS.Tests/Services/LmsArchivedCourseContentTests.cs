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

public sealed class LmsArchivedCourseContentTests
{
    [Fact]
    public async Task Completed_student_reads_only_published_historical_sections_and_safe_resources()
    {
        await using var db = Db();
        var (course, _, _, _) = await SeedAsync(db);
        var result = await Service(db).GetCourseContentAsync(course.PublicId);
        Assert.True(result.Success, result.Message);
        Assert.True(result.Data!.IsArchived);
        Assert.False(result.Data.CanEdit);
        var section = Assert.Single(result.Data.Sections);
        Assert.Equal("Published section", section.Title);
        var lesson = Assert.Single(section.Lessons);
        Assert.Equal("Published lesson", lesson.Title);
        var resource = Assert.Single(lesson.Resources);
        Assert.Equal("https://example.com/notes", resource.ExternalUrl);
        Assert.Empty(result.Data.UnsectionedLessons);
    }

    [Fact]
    public async Task Historical_access_rejects_campus_curriculum_and_withdrawn_academic_enrollment()
    {
        await using var db = Db();
        var (course, academic, _, curriculum) = await SeedAsync(db);
        var service = Service(db);
        academic.CampusId = 999;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetCourseContentAsync(course.PublicId)).StatusCode);
        academic.CampusId = 5;
        curriculum.EffectiveTo = new DateOnly(2024, 1, 1);
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetCourseContentAsync(course.PublicId)).StatusCode);
        curriculum.EffectiveTo = new DateOnly(2024, 12, 31);
        academic.State = EnrollmentState.Withdrawn;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetCourseContentAsync(course.PublicId)).StatusCode);
    }

    [Fact]
    public async Task Historical_access_rejects_active_unlinked_and_incomplete_course_enrollments()
    {
        await using var db = Db();
        var (course, _, enrollment, _) = await SeedAsync(db);
        var service = Service(db);
        enrollment.State = CourseEnrollmentState.Active;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetCourseContentAsync(course.PublicId)).StatusCode);
        enrollment.State = CourseEnrollmentState.Completed;
        enrollment.StudentEnrollmentId = null;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetCourseContentAsync(course.PublicId)).StatusCode);
        enrollment.StudentEnrollmentId = (await db.Set<StudentEnrollment>().SingleAsync()).Id;
        enrollment.CompletedAt = null;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetCourseContentAsync(course.PublicId)).StatusCode);
    }

    private static async Task<(Course Course, StudentEnrollment Academic, CourseEnrollment Enrollment, AcademicCurriculum Curriculum)> SeedAsync(EduOSDbContext db)
    {
        var program = new AcademicProgram { TenantId = 101, Code = "P1", Name = "Science" };
        var subject = new Subject { TenantId = 101, Code = "S1", Name = "Biology" };
        var student = new Student { TenantId = 101, UserId = 51, StudentCode = "ST1",
            FullName = "Historical Student", StatusCode = "Active" };
        db.AddRange(program, subject, student);
        await db.SaveChangesAsync();
        var year = new AcademicYear { TenantId = 101, Code = "Y24", Name = "2024",
            CampusId = 5, StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31) };
        var curriculum = new AcademicCurriculum { TenantId = 101, Code = "C24", Name = "2024 Curriculum",
            AcademicProgramId = program.Id, EffectiveFrom = new DateOnly(2024, 1, 1),
            EffectiveTo = new DateOnly(2024, 12, 31) };
        db.AddRange(year, curriculum);
        await db.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = 101, Code = "B24", Name = "Batch 2024",
            CampusId = 5, AcademicYearId = year.Id, AcademicProgramId = program.Id, AcademicLevelId = 3 };
        var subjectMap = new CurriculumSubject { TenantId = 101, AcademicCurriculumId = curriculum.Id,
            AcademicLevelId = 3, SubjectId = subject.Id };
        var course = new Course { TenantId = 101, Code = "BIO-24", Title = "Historical Biology",
            AcademicProgramId = program.Id, SubjectId = subject.Id, IsActive = false };
        db.AddRange(batch, subjectMap, course);
        await db.SaveChangesAsync();
        var academic = new StudentEnrollment { TenantId = 101, StudentId = student.Id,
            AcademicProgramId = program.Id, AcademicBatchId = batch.Id, AcademicLevelId = 3,
            AcademicYearId = year.Id, AcademicCurriculumId = curriculum.Id, CampusId = 5,
            EnrollmentDate = new DateOnly(2024, 2, 1), State = EnrollmentState.Completed, IsCurrent = false };
        db.Add(academic);
        await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment { TenantId = 101, CourseId = course.Id,
            StudentId = student.Id, StudentEnrollmentId = academic.Id,
            ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Completed,
            EnrolledAt = new DateTime(2024, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2024, 12, 15, 0, 0, 0, DateTimeKind.Utc) };
        var published = new CourseSection { TenantId = 101, CourseId = course.Id,
            Title = "Published section", DisplayOrder = 1, IsPublished = true };
        var draft = new CourseSection { TenantId = 101, CourseId = course.Id,
            Title = "Draft section", DisplayOrder = 2, IsPublished = false };
        db.AddRange(enrollment, published, draft);
        await db.SaveChangesAsync();
        var visible = new Lesson { TenantId = 101, CourseId = course.Id,
            CourseSectionId = published.Id, Title = "Published lesson", IsPublished = true };
        var hidden = new Lesson { TenantId = 101, CourseId = course.Id,
            CourseSectionId = draft.Id, Title = "Hidden lesson", IsPublished = true };
        var unpublished = new Lesson { TenantId = 101, CourseId = course.Id,
            CourseSectionId = published.Id, Title = "Draft lesson", IsPublished = false };
        var unsafeFile = new FileAsset { TenantId = 101, StorageProvider = "Local",
            StorageKey = "private/unsafe", OriginalFileName = "unsafe.pdf",
            ContentType = "application/pdf", IsVerifiedSafe = false };
        db.AddRange(visible, hidden, unpublished, unsafeFile);
        await db.SaveChangesAsync();
        db.AddRange(
            new LessonResource { TenantId = 101, LessonId = visible.Id, Title = "Notes",
                ExternalUrl = "https://example.com/notes", DisplayOrder = 1 },
            new LessonResource { TenantId = 101, LessonId = visible.Id, Title = "Unsafe file",
                FileAssetId = unsafeFile.Id, DisplayOrder = 2 });
        await db.SaveChangesAsync();
        return (course, academic, enrollment, curriculum);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("archived-content-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LmsCourseContentService Service(EduOSDbContext db) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseSection>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonResource>(db),
        new GenericRepository<FileAsset>(db), new GenericRepository<Employee>(db),
        new GenericRepository<Student>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<AcademicBatch>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db), new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<EmployeeCampusAssignment>(db), db, new StudentUser(),
        TimeProvider.System, NullLogger<LmsCourseContentService>.Instance);

    private sealed class StudentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 51;
        public long TenantId => 101;
        public string? FullName => "Historical Student";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Student"];
        public bool IsInRole(string role) => role == "Student";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
