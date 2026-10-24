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

public sealed class LmsCourseCompletionWorkflowTests
{
    [Fact]
    public async Task Published_lesson_completion_is_required_before_recording_completion()
    {
        await using var db = Db();
        var (course, student, enrollment, lesson) = await Seed(db);
        var service = Service(db);
        var before = await service.GetCourseCompletionRegisterAsync(course.PublicId, 1, 20, null);
        Assert.True(before.Success, before.Message);
        Assert.Single(before.Data!.Items);
        Assert.False(before.Data.Items[0].Eligibility.Ready);
        Assert.Equal(409, (await service.CompleteCourseEnrollmentAsync(enrollment.Id,
            new CompleteCourseEnrollmentRequestDto { RowVersion = Convert.ToBase64String(enrollment.RowVersion) })).StatusCode);
        db.Add(new LessonProgress { TenantId = 101, CourseEnrollmentId = enrollment.Id, LessonId = lesson.Id,
            IsCompleted = true, ProgressPercent = 100, CompletedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var result = await service.CompleteCourseEnrollmentAsync(enrollment.Id,
            new CompleteCourseEnrollmentRequestDto { RowVersion = Convert.ToBase64String(enrollment.RowVersion) });
        Assert.True(result.Success, result.Message);
        Assert.Equal(EduOS.Core.Enums.Domain.CourseEnrollmentState.Completed, result.Data!.State);
        Assert.NotNull(result.Data.CompletedAt);
        Assert.True((await service.CompleteCourseEnrollmentAsync(enrollment.Id,
            new CompleteCourseEnrollmentRequestDto { RowVersion = Convert.ToBase64String(enrollment.RowVersion) })).Success);
    }

    [Fact]
    public async Task Published_assignment_and_quiz_must_be_graded_and_quiz_passed()
    {
        await using var db = Db();
        var (course, student, enrollment, lesson) = await Seed(db);
        var assignment = new Assignment { TenantId = 101, CourseId = course.Id, Title = "Essay", IsPublished = true };
        var quiz = new Quiz { TenantId = 101, CourseId = course.Id, Title = "Quiz", IsPublished = true, PassMarks = 6 };
        db.AddRange(assignment, quiz);await db.SaveChangesAsync();
        db.Add(new LessonProgress { TenantId = 101, CourseEnrollmentId = enrollment.Id, LessonId = lesson.Id,
            IsCompleted = true, ProgressPercent = 100 });
        await db.SaveChangesAsync();
        var service = Service(db);
        var request = new CompleteCourseEnrollmentRequestDto { RowVersion = Convert.ToBase64String(enrollment.RowVersion) };
        Assert.Equal(409, (await service.CompleteCourseEnrollmentAsync(enrollment.Id, request)).StatusCode);
        db.Add(new AssignmentSubmission { TenantId = 101, AssignmentId = assignment.Id,
            CourseEnrollmentId = enrollment.Id, State = EduOS.Core.Enums.Domain.LearningSubmissionState.Graded, Marks = 10 });
        db.Add(new QuizAttempt { TenantId = 101, QuizId = quiz.Id, CourseEnrollmentId = enrollment.Id,
            State = EduOS.Core.Enums.Domain.QuizAttemptState.Graded, ObtainedMarks = 5 });
        await db.SaveChangesAsync();
        Assert.Equal(409, (await service.CompleteCourseEnrollmentAsync(enrollment.Id, request)).StatusCode);
        var attempt = await db.Set<QuizAttempt>().SingleAsync();
        attempt.ObtainedMarks = 7;await db.SaveChangesAsync();
        Assert.True((await service.CompleteCourseEnrollmentAsync(enrollment.Id, request)).Success);
    }

    [Fact]
    public async Task Cross_tenant_enrollment_and_student_role_are_denied()
    {
        await using var db = Db();
        var (course, student, enrollment, lesson) = await Seed(db);
        Assert.Equal(403, (await Service(db, "Student").GetCourseCompletionRegisterAsync(course.PublicId, 1, 20, null)).StatusCode);
        Assert.Equal(403, (await Service(db, "Student").CompleteCourseEnrollmentAsync(enrollment.Id,
            new CompleteCourseEnrollmentRequestDto { RowVersion = "AQID" })).StatusCode);
        enrollment.TenantId = 202;await db.SaveChangesAsync();
        Assert.Equal(404, (await Service(db).CompleteCourseEnrollmentAsync(enrollment.Id,
            new CompleteCourseEnrollmentRequestDto { RowVersion = "AQID" })).StatusCode);
    }

    private static async Task<(Course Course, Student Student, CourseEnrollment Enrollment, Lesson Lesson)> Seed(EduOSDbContext db)
    {
        var course = new Course { TenantId = 101, Code = "LMS-COMP", Title = "Completion", IsActive = true };
        var student = new Student { TenantId = 101, StudentCode = "S-101", FullName = "Learner", UserId = 77, StatusCode = "Active" };
        db.AddRange(course, student);await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment { TenantId = 101, CourseId = course.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), RowVersion = [1,2,3] };
        var lesson = new Lesson { TenantId = 101, CourseId = course.Id, Title = "Introduction", IsPublished = true };
        db.AddRange(enrollment, lesson);await db.SaveChangesAsync();
        return (course, student, enrollment, lesson);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-completion-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LmsWorkflowService Service(EduOSDbContext db, string role = "TenantAdmin") => new(
        new GenericRepository<Course>(db), new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Lesson>(db), new GenericRepository<LessonProgress>(db),
        new GenericRepository<Assignment>(db), new GenericRepository<AssignmentSubmission>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Employee>(db), new GenericRepository<AcademicProgram>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<FileAsset>(db),
        db, new TestUser(role), TimeProvider.System, NullLogger<LmsWorkflowService>.Instance,
        completionQuizzes: new GenericRepository<Quiz>(db),
        completionAttempts: new GenericRepository<QuizAttempt>(db));

    private sealed class TestUser(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 77;
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
