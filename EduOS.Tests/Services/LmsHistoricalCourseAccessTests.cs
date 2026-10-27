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

public sealed class LmsHistoricalCourseAccessTests
{
    [Fact]
    public async Task Completed_student_can_find_archived_course_and_only_published_content()
    {
        await using var db = Db();
        var (course, _, _) = await Seed(db);
        var service = Service(db);
        var current = await service.GetMyCoursesAsync(1, 20);
        Assert.True(current.Success, current.Message);
        Assert.Empty(current.Data!.Items);
        var history = await service.GetMyCoursesAsync(1, 20, default, true);
        Assert.True(history.Success, history.Message);
        Assert.Single(history.Data!.Items);
        Assert.Equal(course.PublicId, history.Data.Items[0].Reference);
        var detail = await service.GetCourseDetailsAsync(course.PublicId);
        Assert.True(detail.Success, detail.Message);
        Assert.Single(detail.Data!.Lessons);
        Assert.Single(detail.Data.Assignments);
        Assert.All(detail.Data.Lessons, x => Assert.True(x.IsPublished));
        Assert.All(detail.Data.Assignments, x => Assert.True(x.IsPublished));
    }

    [Fact]
    public async Task Course_content_pages_preserve_published_visibility_and_counts()
    {
        await using var db = Db();
        var (course, _, _) = await Seed(db);
        var service = Service(db);
        var first = await service.GetCourseContentPageAsync(course.PublicId, 1, 1, 1);
        Assert.True(first.Success, first.Message);
        Assert.Equal(1, first.Data!.Lessons.TotalCount);
        Assert.Equal(1, first.Data.Assignments.TotalCount);
        Assert.Single(first.Data.Lessons.Items);
        Assert.Single(first.Data.Assignments.Items);
        Assert.True(first.Data.Lessons.Items[0].IsPublished);
        Assert.True(first.Data.Assignments.Items[0].IsPublished);
        var second = await service.GetCourseContentPageAsync(course.PublicId, 2, 2, 1);
        Assert.True(second.Success, second.Message);
        Assert.Empty(second.Data!.Lessons.Items);
        Assert.Empty(second.Data.Assignments.Items);
        Assert.Equal(1, second.Data.Lessons.TotalCount);
        Assert.Equal(400, (await service.GetCourseContentPageAsync(course.PublicId, 0, 1, 20)).StatusCode);
        Assert.Equal(400, (await service.GetCourseContentPageAsync(course.PublicId, 1, 1, 101)).StatusCode);
        Assert.Equal(404, (await service.GetCourseContentPageAsync(Guid.NewGuid(), 1, 1, 20)).StatusCode);
    }

    [Fact]
    public async Task Archived_course_requires_original_campus_and_curriculum_date()
    {
        await using var db = Db();
        var (course, academic, _) = await Seed(db);
        var service = Service(db);
        academic.CampusId = 900;
        await db.SaveChangesAsync();
        Assert.Empty((await service.GetMyCoursesAsync(1, 20, default, true)).Data!.Items);
        Assert.Equal(404, (await service.GetCourseDetailsAsync(course.PublicId)).StatusCode);
        academic.CampusId = 5;
        var curriculum = await db.Set<AcademicCurriculum>().SingleAsync();
        curriculum.EffectiveTo = new DateOnly(2024, 1, 1);
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetCourseDetailsAsync(course.PublicId)).StatusCode);
    }

    [Fact]
    public async Task Withdrawn_or_active_course_enrollment_cannot_expose_archived_course()
    {
        await using var db = Db();
        var (course, academic, enrollment) = await Seed(db);
        var service = Service(db);
        enrollment.State = CourseEnrollmentState.Active;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetCourseDetailsAsync(course.PublicId)).StatusCode);
        enrollment.State = CourseEnrollmentState.Completed;
        academic.State = EnrollmentState.Withdrawn;
        await db.SaveChangesAsync();
        Assert.Empty((await service.GetMyCoursesAsync(1, 20, default, true)).Data!.Items);
        Assert.Equal(404, (await service.GetCourseDetailsAsync(course.PublicId)).StatusCode);
    }

    [Fact]
    public async Task Historical_access_fails_closed_when_optional_academic_repositories_are_missing()
    {
        await using var db = Db();
        var (course, _, _) = await Seed(db);
        var service = Service(db, withAcademicRepositories: false);
        Assert.Empty((await service.GetMyCoursesAsync(1, 20, default, true)).Data!.Items);
        Assert.Equal(404, (await service.GetCourseDetailsAsync(course.PublicId)).StatusCode);
    }

    private static async Task<(Course Course, StudentEnrollment Academic, CourseEnrollment Enrollment)> Seed(EduOSDbContext db)
    {
        var program = new AcademicProgram { TenantId = 101, Name = "Science", Code = "SCI" };
        var subject = new Subject { TenantId = 101, Name = "Biology", Code = "BIO" };
        var student = new Student { TenantId = 101, StudentCode = "H-1", FullName = "Historical Learner",
            UserId = 77, StatusCode = "Active" };
        db.AddRange(program, subject, student);
        await db.SaveChangesAsync();
        var year = new AcademicYear { TenantId = 101, Name = "2024", Code = "2024",
            CampusId = 5, StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31) };
        var curriculum = new AcademicCurriculum { TenantId = 101, AcademicProgramId = program.Id,
            Name = "Science 2024", Code = "SC24", EffectiveFrom = new DateOnly(2024, 1, 1),
            EffectiveTo = new DateOnly(2024, 12, 31) };
        db.AddRange(year, curriculum);
        await db.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = 101, CampusId = 5, AcademicYearId = year.Id,
            AcademicProgramId = program.Id, AcademicLevelId = 3, Name = "Year 1", Code = "Y1" };
        var curriculumSubject = new CurriculumSubject { TenantId = 101,
            AcademicCurriculumId = curriculum.Id, AcademicLevelId = 3, SubjectId = subject.Id };
        var course = new Course { TenantId = 101, Code = "BIO-ARCH", Title = "Archived Biology",
            AcademicProgramId = program.Id, SubjectId = subject.Id, IsActive = false };
        db.AddRange(batch, curriculumSubject, course);
        await db.SaveChangesAsync();
        var academic = new StudentEnrollment { TenantId = 101, StudentId = student.Id,
            AcademicProgramId = program.Id, AcademicBatchId = batch.Id, AcademicLevelId = 3,
            AcademicYearId = year.Id, AcademicCurriculumId = curriculum.Id, CampusId = 5,
            EnrollmentDate = new DateOnly(2024, 2, 1), State = EnrollmentState.Completed,
            IsCurrent = false };
        db.Add(academic);
        await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment { TenantId = 101, CourseId = course.Id,
            StudentId = student.Id, StudentEnrollmentId = academic.Id,
            ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Completed,
            EnrolledAt = new DateTime(2024, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2024, 12, 15, 0, 0, 0, DateTimeKind.Utc) };
        db.AddRange(enrollment,
            new Lesson { TenantId = 101, CourseId = course.Id, Title = "Published lesson", IsPublished = true },
            new Lesson { TenantId = 101, CourseId = course.Id, Title = "Draft lesson", IsPublished = false },
            new Assignment { TenantId = 101, CourseId = course.Id, Title = "Published assignment", IsPublished = true },
            new Assignment { TenantId = 101, CourseId = course.Id, Title = "Draft assignment", IsPublished = false });
        await db.SaveChangesAsync();
        return (course, academic, enrollment);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-history-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LmsWorkflowService Service(EduOSDbContext db, bool withAcademicRepositories = true) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Employee>(db), new GenericRepository<AcademicProgram>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<FileAsset>(db),
        db, new StudentUser(), TimeProvider.System, NullLogger<LmsWorkflowService>.Instance,
        submissionYears: withAcademicRepositories ? new GenericRepository<AcademicYear>(db) : null,
        submissionTerms: withAcademicRepositories ? new GenericRepository<AcademicTerm>(db) : null,
        submissionBatches: withAcademicRepositories ? new GenericRepository<AcademicBatch>(db) : null,
        submissionCurricula: withAcademicRepositories ? new GenericRepository<AcademicCurriculum>(db) : null);

    private sealed class StudentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 77;
        public long TenantId => 101;
        public string? FullName => "Historical Learner";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Student"];
        public bool IsInRole(string role) => role == "Student";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
