using EduOS.Core.Entities.Academic;
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
using System.Text.Json;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LmsQuizHistoricalEvidenceTests
{
    [Fact]
    public async Task Completed_student_can_search_archived_graded_attempts_without_answer_keys()
    {
        await using var db = Db();
        var seed = await Seed(db);
        var service = Service(db);
        var response = await service.GetMyQuizHistoryAsync(seed.Course.PublicId);
        Assert.True(response.Success, response.Message);
        var row = Assert.Single(response.Data!.Items);
        Assert.Equal(seed.Attempt.Id, row.AttemptId);
        Assert.True(row.Passed);
        Assert.True(row.IsArchived);
        Assert.DoesNotContain("IsCorrect", JsonSerializer.Serialize(row));
        Assert.DoesNotContain("SelectedOption", JsonSerializer.Serialize(row));
        Assert.True((await service.GetMyAttemptReviewAsync(seed.Attempt.Id)).Success);
        Assert.Equal(409, (await service.StartQuizAttemptAsync(new()
        {
            QuizReference = seed.Quiz.PublicId, CourseEnrollmentId = seed.Enrollment.Id,
            ClientRequestId = Guid.NewGuid()
        })).StatusCode);
    }

    [Fact]
    public async Task Historical_campus_curriculum_and_withdrawal_revoke_attempt_evidence()
    {
        await using var db = Db();
        var seed = await Seed(db);
        var service = Service(db);
        seed.Academic.CampusId = 99;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetMyQuizHistoryAsync(seed.Course.PublicId)).StatusCode);
        Assert.Equal(403, (await service.GetMyAttemptReviewAsync(seed.Attempt.Id)).StatusCode);
        seed.Academic.CampusId = 5;
        var curriculum = await db.Set<AcademicCurriculum>().SingleAsync();
        curriculum.EffectiveTo = new DateOnly(2024, 1, 1);
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetMyQuizHistoryAsync(seed.Course.PublicId)).StatusCode);
        curriculum.EffectiveTo = new DateOnly(2024, 12, 31);
        seed.Academic.State = EnrollmentState.Withdrawn;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetMyQuizHistoryAsync(seed.Course.PublicId)).StatusCode);
    }

    [Fact]
    public async Task Foreign_student_and_missing_historical_repositories_fail_closed()
    {
        await using var db = Db();
        var seed = await Seed(db);
        Assert.Equal(404, (await Service(db, 999).GetMyQuizHistoryAsync(seed.Course.PublicId)).StatusCode);
        Assert.Equal(403, (await Service(db, 999).GetMyAttemptReviewAsync(seed.Attempt.Id)).StatusCode);
        Assert.Equal(404, (await Service(db, 77, false).GetMyQuizHistoryAsync(seed.Course.PublicId)).StatusCode);
        Assert.Equal(403, (await Service(db, 77, false).GetMyAttemptReviewAsync(seed.Attempt.Id)).StatusCode);
    }

    [Fact]
    public async Task Completed_enrollment_is_read_only_even_when_course_remains_active()
    {
        await using var db = Db();
        var seed = await Seed(db);
        seed.Course.IsActive = true;
        await db.SaveChangesAsync();
        var service = Service(db);
        var access = await service.GetMyQuizAttemptEligibilityAsync(seed.Course.PublicId);
        Assert.True(access.Success, access.Message);
        Assert.False(access.Data!.CanAttempt);
        Assert.Equal(0L, access.Data.CourseEnrollmentId);
        Assert.Equal(403, (await service.GetAvailableQuizzesAsync(seed.Course.PublicId)).StatusCode);
        Assert.Equal(403, (await service.GetQuizForLearnerAsync(seed.Quiz.PublicId)).StatusCode);
        Assert.Equal(403, (await service.GetMyCourseEnrollmentIdAsync(seed.Course.PublicId)).StatusCode);
        Assert.True((await service.GetMyQuizHistoryAsync(seed.Course.PublicId)).Success);
    }

    [Fact]
    public async Task Active_enrollment_can_list_quizzes_and_resolve_candidate_access()
    {
        await using var db = Db();
        var seed = await Seed(db);
        seed.Course.IsActive = true;
        seed.Enrollment.State = CourseEnrollmentState.Active;
        seed.Enrollment.CompletedAt = null;
        seed.Academic.State = EnrollmentState.Active;
        await db.SaveChangesAsync();
        var service = Service(db);
        var access = await service.GetMyQuizAttemptEligibilityAsync(seed.Course.PublicId);
        Assert.True(access.Success, access.Message);
        Assert.True(access.Data!.CanAttempt);
        Assert.Equal(seed.Enrollment.Id, access.Data.CourseEnrollmentId);
        var available = await service.GetAvailableQuizzesAsync(seed.Course.PublicId);
        Assert.True(available.Success, available.Message);
        Assert.Single(available.Data!);
        Assert.True((await service.GetQuizForLearnerAsync(seed.Quiz.PublicId)).Success);
        Assert.Equal(seed.Enrollment.Id,
            (await service.GetMyCourseEnrollmentIdAsync(seed.Course.PublicId)).Data);
    }

    [Fact]
    public async Task Archived_quiz_eligibility_requires_original_campus_and_curriculum()
    {
        await using var db = Db();
        var seed = await Seed(db);
        var service = Service(db);
        var access = await service.GetMyQuizAttemptEligibilityAsync(seed.Course.PublicId);
        Assert.True(access.Success, access.Message);
        Assert.False(access.Data!.CanAttempt);
        seed.Academic.CampusId = 99;
        await db.SaveChangesAsync();
        Assert.Equal(403,
            (await service.GetMyQuizAttemptEligibilityAsync(seed.Course.PublicId)).StatusCode);
        Assert.Equal(403,
            (await Service(db, 999).GetMyQuizAttemptEligibilityAsync(seed.Course.PublicId)).StatusCode);
    }

    private static async Task<(Course Course, Quiz Quiz, CourseEnrollment Enrollment,
        StudentEnrollment Academic, QuizAttempt Attempt)> Seed(EduOSDbContext db)
    {
        var program = new AcademicProgram { TenantId = 101, Name = "Science", Code = "SCI" };
        var subject = new Subject { TenantId = 101, Name = "Biology", Code = "BIO" };
        var student = new Student { TenantId = 101, StudentCode = "H-77",
            FullName = "Learner", UserId = 77, StatusCode = "Active" };
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
            EnrollmentDate = new DateOnly(2024, 2, 1), State = EnrollmentState.Completed };
        db.Add(academic);
        await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment { TenantId = 101, CourseId = course.Id,
            StudentId = student.Id, StudentEnrollmentId = academic.Id,
            ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Completed,
            EnrolledAt = new DateTime(2024, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2024, 12, 15, 0, 0, 0, DateTimeKind.Utc) };
        var quiz = new Quiz { TenantId = 101, CourseId = course.Id, Title = "Final Quiz",
            IsPublished = true, DurationMinutes = 30, PassMarks = 5, MaxAttempts = 2 };
        db.AddRange(enrollment, quiz);
        await db.SaveChangesAsync();
        var attempt = new QuizAttempt { TenantId = 101, QuizId = quiz.Id,
            CourseEnrollmentId = enrollment.Id, ClientRequestId = Guid.NewGuid(),
            AttemptNo = 1, State = QuizAttemptState.Graded, ObtainedMarks = 8,
            StartedAt = new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            SubmittedAt = new DateTime(2024, 11, 1, 0, 20, 0, DateTimeKind.Utc) };
        db.Add(attempt);
        await db.SaveChangesAsync();
        return (course, quiz, enrollment, academic, attempt);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-quiz-history-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LmsAssessmentService Service(EduOSDbContext db, long userId = 77,
        bool withAcademicRepositories = true) => new(
        new GenericRepository<Course>(db), new GenericRepository<Quiz>(db),
        new GenericRepository<QuizQuestion>(db), new GenericRepository<QuizOption>(db),
        new GenericRepository<QuizAttempt>(db), new GenericRepository<QuizAnswer>(db),
        new GenericRepository<CourseEnrollment>(db), new GenericRepository<Student>(db),
        new GenericRepository<Employee>(db), db, new StudentUser(userId), TimeProvider.System,
        NullLogger<LmsAssessmentService>.Instance, new GenericRepository<QuizAnswerOption>(db),
        historyAcademics: withAcademicRepositories ? new GenericRepository<StudentEnrollment>(db) : null,
        historyBatches: withAcademicRepositories ? new GenericRepository<AcademicBatch>(db) : null,
        historyYears: withAcademicRepositories ? new GenericRepository<AcademicYear>(db) : null,
        historyCurricula: withAcademicRepositories ? new GenericRepository<AcademicCurriculum>(db) : null,
        historyTerms: withAcademicRepositories ? new GenericRepository<AcademicTerm>(db) : null,
        historySubjects: withAcademicRepositories ? new GenericRepository<CurriculumSubject>(db) : null);

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
