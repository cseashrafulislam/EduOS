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

public sealed class LmsLearnerProgressTests
{
    [Fact]
    public async Task Learner_sees_paged_published_progress_from_only_their_own_enrollments()
    {
        await using var db = Db();
        var student = new Student { TenantId = 101, UserId = 77, StudentCode = "S-77",
            FullName = "Learner", StatusCode = "Active" };
        var other = new Student { TenantId = 101, UserId = 88, StudentCode = "S-88",
            FullName = "Other learner", StatusCode = "Active" };
        var course = new Course { TenantId = 101, Code = "LMS-A", Title = "Course A" };
        var second = new Course { TenantId = 101, Code = "LMS-B", Title = "Course B" };
        db.AddRange(student, other, course, second);
        await db.SaveChangesAsync();
        var mine = new CourseEnrollment { TenantId = 101, StudentId = student.Id,
            CourseId = course.Id, ClientRequestId = Guid.NewGuid(),
            State = CourseEnrollmentState.Active, EnrolledAt = DateTime.UtcNow };
        var mineSecond = new CourseEnrollment { TenantId = 101, StudentId = student.Id,
            CourseId = second.Id, ClientRequestId = Guid.NewGuid(),
            State = CourseEnrollmentState.Completed, EnrolledAt = DateTime.UtcNow.AddDays(-1) };
        var notMine = new CourseEnrollment { TenantId = 101, StudentId = other.Id,
            CourseId = course.Id, ClientRequestId = Guid.NewGuid(),
            State = CourseEnrollmentState.Active };
        var published = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Visible",
            IsPublished = true, DisplayOrder = 1 };
        var draft = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Hidden",
            IsPublished = false, DisplayOrder = 2 };
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id,
            Title = "Graded task", IsPublished = true };
        var quiz = new Quiz { TenantId = 101, CourseId = course.Id,
            Title = "Final", IsPublished = true, PassMarks = 6 };
        db.AddRange(mine, mineSecond, notMine, published, draft, assignment, quiz);
        await db.SaveChangesAsync();
        db.AddRange(
            new LessonProgress { TenantId = 101, CourseEnrollmentId = mine.Id,
                LessonId = published.Id, IsCompleted = true, ProgressPercent = 100 },
            new LessonProgress { TenantId = 101, CourseEnrollmentId = notMine.Id,
                LessonId = draft.Id, IsCompleted = true, ProgressPercent = 100 },
            new LessonProgress { TenantId = 101, CourseEnrollmentId = mine.Id,
                LessonId = draft.Id, IsCompleted = true, ProgressPercent = 100 },
            new AssignmentSubmission { TenantId = 101, CourseEnrollmentId = mine.Id,
                AssignmentId = assignment.Id, State = LearningSubmissionState.Graded, Marks = 9 },
            new QuizAttempt { TenantId = 101, CourseEnrollmentId = mine.Id,
                QuizId = quiz.Id, ClientRequestId = Guid.NewGuid(),
                State = QuizAttemptState.Graded, ObtainedMarks = 8 });
        await db.SaveChangesAsync();

        var service = Service(db);
        var first = await service.GetMyCourseProgressAsync(1, 1);
        Assert.True(first.Success, first.Message);
        Assert.Equal(2, first.Data!.TotalCount);
        var row = Assert.Single(first.Data.Items);
        Assert.Equal(course.PublicId, row.CourseReference);
        Assert.Equal(1, row.RequiredLessons);
        Assert.Equal(1, row.CompletedLessons);
        Assert.Equal(1, row.RequiredAssignments);
        Assert.Equal(1, row.GradedAssignments);
        Assert.Equal(1, row.RequiredQuizzes);
        Assert.Equal(1, row.PassedQuizzes);
        Assert.Equal(100m, row.LessonCompletionPercent);
        var next = await service.GetMyCourseProgressAsync(2, 1);
        Assert.Equal(second.PublicId, Assert.Single(next.Data!.Items).CourseReference);
        Assert.Equal(0m, next.Data.Items[0].LessonCompletionPercent);
        Assert.Equal(400, (await service.GetMyCourseProgressAsync(1, 26)).StatusCode);
        Assert.Equal(400, (await service.GetMyCourseProgressAsync(int.MaxValue, 25)).StatusCode);
    }

    [Fact]
    public async Task Lesson_readback_hides_drafts_and_other_students_progress()
    {
        await using var db = Db();
        var owner = new Student { TenantId = 101, UserId = 77, StudentCode = "OWNER",
            FullName = "Owner", StatusCode = "Active" };
        var other = new Student { TenantId = 101, UserId = 88, StudentCode = "OTHER",
            FullName = "Other", StatusCode = "Active" };
        var course = new Course { TenantId = 101, Code = "COURSE", Title = "Course" };
        var inaccessible = new Course { TenantId = 101, Code = "PRIVATE", Title = "Private" };
        db.AddRange(owner, other, course, inaccessible);
        await db.SaveChangesAsync();
        var mine = new CourseEnrollment { TenantId = 101, StudentId = owner.Id,
            CourseId = course.Id, ClientRequestId = Guid.NewGuid() };
        var theirs = new CourseEnrollment { TenantId = 101, StudentId = other.Id,
            CourseId = inaccessible.Id, ClientRequestId = Guid.NewGuid() };
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id,
            Title = "Published", IsPublished = true };
        var draft = new Lesson { TenantId = 101, CourseId = course.Id,
            Title = "Draft", IsPublished = false };
        db.AddRange(mine, theirs, lesson, draft);
        await db.SaveChangesAsync();
        db.Add(new LessonProgress { TenantId = 101, CourseEnrollmentId = mine.Id,
            LessonId = lesson.Id, ProgressPercent = 60, IsCompleted = false });
        await db.SaveChangesAsync();
        var service = Service(db);
        var result = await service.GetMyLessonProgressAsync(course.PublicId, 1, 20);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.TotalCount);
        var progress = Assert.Single(result.Data.Items);
        Assert.Equal(60m, progress.ProgressPercent);
        Assert.False(progress.IsCompleted);
        Assert.Equal(404, (await service.GetMyLessonProgressAsync(inaccessible.PublicId, 1, 20)).StatusCode);
        Assert.Equal(404, (await Service(db, 999).GetMyLessonProgressAsync(course.PublicId, 1, 20)).StatusCode);
        Assert.Equal(403, (await Service(db, 77, "Teacher").GetMyCourseProgressAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await Service(db, 77, "Teacher").GetMyLessonProgressAsync(course.PublicId, 1, 20)).StatusCode);
    }

    [Fact]
    public async Task Missing_quiz_repositories_fail_closed_without_partial_progress()
    {
        await using var db = Db();
        var response = await Service(db, withQuizzes: false).GetMyCourseProgressAsync(1, 20);
        Assert.Equal(503, response.StatusCode);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-progress-" + Guid.NewGuid().ToString("N")).Options;
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = context });
    }

    private static LmsWorkflowService Service(EduOSDbContext db, long userId = 77,
        string role = "Student", bool withQuizzes = true) => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Employee>(db), new GenericRepository<AcademicProgram>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<FileAsset>(db),
        db, new TestUser(userId, role), TimeProvider.System,
        NullLogger<LmsWorkflowService>.Instance,
        completionQuizzes: withQuizzes ? new GenericRepository<Quiz>(db) : null,
        completionAttempts: withQuizzes ? new GenericRepository<QuizAttempt>(db) : null);

    private sealed class TestUser(long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => 101;
        public string? FullName => "Learner";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
