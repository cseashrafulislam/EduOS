using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AdaptiveStudyServiceTests
{
    [Fact]
    public async Task Learner_plan_uses_latest_graded_attempt_and_teacher_target_without_editing_marks()
    {
        await using var db = Db();
        var seed = await Seed(db);
        db.AdaptiveStudyGoals.Add(new AdaptiveStudyGoal
        {
            TenantId = 101, CourseId = seed.Course.Id, StudentId = seed.Student.Id,
            CourseSectionId = seed.Chapter.Id, SkillCode = "ALGEBRA-01",
            TargetPercent = 75, SetByUserId = 66, TeacherNote = "Revise fractions"
        });
        await db.SaveChangesAsync();
        var result = await Service(db, 44, "Student").CurrentAsync(seed.Course.PublicId);
        Assert.True(result.Success, result.Message);
        var skill = Assert.Single(result.Data!.Skills);
        Assert.Equal("ALGEBRA-01", skill.SkillCode);
        Assert.Equal(70m, skill.ObservedPercent);
        Assert.Equal(75m, skill.TargetPercent);
        Assert.Equal("Practice", skill.Priority);
        Assert.Equal(2, skill.GradedAttempts);
        Assert.Equal(1, skill.PracticeAttemptsRemaining);
        Assert.Equal(seed.Quiz.PublicId, skill.RecommendedQuizReference);
        Assert.Equal("Revise fractions", skill.TeacherNote);
        Assert.Equal(7m, (await db.QuizAttempts.SingleAsync(x => x.AttemptNo == 2)).ObtainedMarks);
    }

    [Fact]
    public async Task Snapshot_is_idempotent_and_preserves_original_result_when_grades_change()
    {
        await using var db = Db();
        var seed = await Seed(db);
        var learner = Service(db, 44, "Student");
        var request = new SaveAdaptivePlanDto
        { CourseReference = seed.Course.PublicId, ClientRequestId = Guid.NewGuid() };
        var first = await learner.SavePlanAsync(request);
        Assert.True(first.Success, first.Message);
        Assert.NotNull(first.Data!.HistoryId);
        var second = await db.QuizAttempts.SingleAsync(x => x.AttemptNo == 2);
        second.ObtainedMarks = 9m;
        await db.SaveChangesAsync();
        var replay = await learner.SavePlanAsync(request);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(first.Data.HistoryId, replay.Data!.HistoryId);
        Assert.Equal(70m, Assert.Single(replay.Data.Skills).ObservedPercent);
        Assert.Equal(90m, Assert.Single((await learner.CurrentAsync(seed.Course.PublicId)).Data!.Skills).ObservedPercent);
        Assert.Single(await db.AdaptiveStudyPlanHistories.ToListAsync());
        Assert.Equal(1, (await learner.HistoryAsync(seed.Course.PublicId, 1, 15)).Data!.TotalCount);
        Assert.Equal(70m, Assert.Single((await learner.HistoricalAsync(first.Data.HistoryId!.Value)).Data!.Skills).ObservedPercent);
    }

    [Fact]
    public async Task Learner_access_is_limited_to_own_tenant_enrollment_and_history()
    {
        await using var db = Db();
        var seed = await Seed(db);
        var owner = Service(db, 44, "Student");
        var saved = await owner.SavePlanAsync(new SaveAdaptivePlanDto
        { CourseReference = seed.Course.PublicId, ClientRequestId = Guid.NewGuid() });
        Assert.True(saved.Success);
        Assert.Equal(403, (await Service(db, 998, "Student").CurrentAsync(seed.Course.PublicId)).StatusCode);
        Assert.Equal(404, (await Service(db, 998, "Student")
            .HistoricalAsync(saved.Data!.HistoryId!.Value)).StatusCode);
        Assert.Equal(403, (await Service(db, 44, "Teacher")
            .CurrentAsync(seed.Course.PublicId)).StatusCode);
        Assert.Equal(404, (await Service(db, 44, "Student", tenantId: 999)
            .HistoricalAsync(saved.Data.HistoryId.Value)).StatusCode);
    }

    [Fact]
    public async Task Teacher_may_set_only_mapped_skills_for_own_course()
    {
        await using var db = Db();
        var seed = await Seed(db);
        var request = new SaveAdaptiveGoalDto
        {
            CourseReference = seed.Course.PublicId, StudentReference = seed.Student.PublicId,
            CourseSectionId = seed.Chapter.Id, SkillCode = "algebra-01", TargetPercent = 85,
            TeacherNote = "Try basic problems"
        };
        var foreign = await Service(db, 77, "Teacher").SaveGoalAsync(request);
        Assert.Equal(403, foreign.StatusCode);
        var unmapped = await Service(db, 66, "Teacher").SaveGoalAsync(
            new SaveAdaptiveGoalDto
            {
                CourseReference = request.CourseReference, StudentReference = request.StudentReference,
                CourseSectionId = request.CourseSectionId, SkillCode = "UNKNOWN",
                TargetPercent = 90
            });
        Assert.Equal(404, unmapped.StatusCode);
        var valid = await Service(db, 66, "Teacher").SaveGoalAsync(request);
        Assert.True(valid.Success, valid.Message);
        Assert.Equal("ALGEBRA-01", valid.Data!.SkillCode);
        Assert.Equal(85m, Assert.Single((await Service(db, 44, "Student")
            .CurrentAsync(seed.Course.PublicId)).Data!.Skills).TargetPercent);
    }

    private static async Task<(Course Course, CourseSection Chapter, Quiz Quiz, Student Student)> Seed(EduOSDbContext db)
    {
        var student = new Student
        { TenantId = 101, UserId = 44, StudentCode = "S-44", FullName = "Learner", StatusCode = "Active" };
        var employee = new Employee
        { TenantId = 101, UserId = 66, EmployeeCode = "T-66", FullName = "Teacher", CanTeach = true, State = EmployeeState.Active };
        db.AddRange(student, employee);
        await db.SaveChangesAsync();
        var course = new Course
        { TenantId = 101, Code = "MATH", Title = "Math", IsActive = true, PrimaryInstructorEmployeeId = employee.Id };
        db.Add(course);
        await db.SaveChangesAsync();
        var chapter = new CourseSection
        { TenantId = 101, CourseId = course.Id, Title = "Fractions", IsPublished = true };
        var enrollment = new CourseEnrollment
        { TenantId = 101, CourseId = course.Id, StudentId = student.Id, State = CourseEnrollmentState.Active };
        db.AddRange(chapter, enrollment);
        await db.SaveChangesAsync();
        var quiz = new Quiz
        {
            TenantId = 101, CourseId = course.Id, CourseSectionId = chapter.Id, SkillCode = "ALGEBRA-01",
            Title = "Fractions practice", IsPublished = true, MaxAttempts = 4, PracticeDifficulty = 1
        };
        db.Add(quiz);
        await db.SaveChangesAsync();
        db.QuizQuestions.Add(new QuizQuestion { TenantId = 101, QuizId = quiz.Id,
            QuestionText = "1 + 1?", Marks = 10 });
        db.QuizAttempts.AddRange(
            new QuizAttempt { TenantId = 101, QuizId = quiz.Id, CourseEnrollmentId = enrollment.Id,
                AttemptNo = 1, State = QuizAttemptState.Graded, ObtainedMarks = 2,
                SubmittedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
            new QuizAttempt { TenantId = 101, QuizId = quiz.Id, CourseEnrollmentId = enrollment.Id,
                AttemptNo = 2, State = QuizAttemptState.Graded, ObtainedMarks = 7,
                SubmittedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc) },
            new QuizAttempt { TenantId = 101, QuizId = quiz.Id, CourseEnrollmentId = enrollment.Id,
                AttemptNo = 3, State = QuizAttemptState.Started, ObtainedMarks = null });
        await db.SaveChangesAsync();
        return (course, chapter, quiz, student);
    }

    private static EduOSDbContext Db()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("adaptive-study-" + Guid.NewGuid().ToString("N")).Options;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static AdaptiveStudyService Service(EduOSDbContext db, long userId,
        string role, long tenantId = 101) => new(db, new TestUser(tenantId, userId, role),
        TimeProvider.System, NullLogger<AdaptiveStudyService>.Instance);

    private sealed class TestUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenantId;
        public long UserId => userId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string checkedRole) => checkedRole == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
