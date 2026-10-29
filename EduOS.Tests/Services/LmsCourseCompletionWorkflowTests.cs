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
    public async Task Student_role_and_unavailable_enrollment_are_denied()
    {
        await using var db = Db();
        var (course, student, enrollment, lesson) = await Seed(db);
        Assert.Equal(403, (await Service(db, "Student").GetCourseCompletionRegisterAsync(course.PublicId, 1, 20, null)).StatusCode);
        Assert.Equal(403, (await Service(db, "Student").CompleteCourseEnrollmentAsync(enrollment.Id,
            new CompleteCourseEnrollmentRequestDto { RowVersion = "AQID" })).StatusCode);
        Assert.Equal(404, (await Service(db).CompleteCourseEnrollmentAsync(enrollment.Id + 1000,
            new CompleteCourseEnrollmentRequestDto { RowVersion = "AQID" })).StatusCode);
    }

    [Fact]
    public async Task Completed_enrollment_blocks_course_lesson_and_assignment_authoring()
    {
        await using var db = Db();
        var (course, _, enrollment, _) = await Seed(db);
        course.RowVersion = [1, 2, 3];
        enrollment.State = EduOS.Core.Enums.Domain.CourseEnrollmentState.Completed;
        enrollment.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var service = Service(db);
        var status = await service.GetCourseAuthoringLockAsync(course.PublicId);
        Assert.True(status.Success, status.Message);
        Assert.True(status.Data!.IsLocked);
        Assert.Equal(409, (await service.SaveCourseAsync(course.PublicId, new SaveCourseRequestDto
        { Code = course.Code, Title = "Changed", RowVersion = Convert.ToBase64String(course.RowVersion) })).StatusCode);
        Assert.Equal(409, (await service.SaveLessonAsync(null, new SaveLessonRequestDto
        { CourseReference = course.PublicId, Title = "New lesson", DisplayOrder = 2 })).StatusCode);
        Assert.Equal(409, (await service.SaveAssignmentAsync(null, new SaveAssignmentRequestDto
        { CourseReference = course.PublicId, Title = "New assignment", MaxMarks = 100 })).StatusCode);
        Assert.Single(await db.Set<Lesson>().ToListAsync());
        Assert.Empty(await db.Set<Assignment>().ToListAsync());
    }

    [Fact]
    public async Task Foreign_tenant_completion_does_not_lock_local_course()
    {
        await using var db = Db();
        var (course, _, _, _) = await Seed(db);
        db.Add(new CourseEnrollment { TenantId = 202, CourseId = course.Id,
            StudentId = 999, State = EduOS.Core.Enums.Domain.CourseEnrollmentState.Completed });
        await TestTenantSeeder.SaveAsync(db);
        var service = Service(db);
        var status = await service.GetCourseAuthoringLockAsync(course.PublicId);
        Assert.True(status.Success, status.Message);
        Assert.False(status.Data!.IsLocked);
        Assert.Equal(403, (await Service(db, "Student").GetCourseAuthoringLockAsync(course.PublicId)).StatusCode);
    }

    [Fact]
    public async Task Assignment_evidence_prevents_rewriting_deadline_and_maximum_marks()
    {
        await using var db = Db();
        var (course, _, enrollment, _) = await Seed(db);
        var assignment = new Assignment
        {
            TenantId = 101, CourseId = course.Id, Title = "Original task",
            MaxMarks = 20, IsPublished = true, RowVersion = [1, 2, 3]
        };
        db.Add(assignment);
        await db.SaveChangesAsync();
        db.Add(new AssignmentSubmission
        {
            TenantId = 101, AssignmentId = assignment.Id,
            CourseEnrollmentId = enrollment.Id,
            State = EduOS.Core.Enums.Domain.LearningSubmissionState.Submitted,
            SubmissionText = "Submitted evidence"
        });
        await db.SaveChangesAsync();

        var result = await Service(db).SaveAssignmentAsync(assignment.PublicId,
            new SaveAssignmentRequestDto
            {
                CourseReference = course.PublicId, Title = "Changed task",
                MaxMarks = 100, IsPublished = false,
                RowVersion = Convert.ToBase64String(assignment.RowVersion)
            });
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Original task", assignment.Title);
        Assert.Equal(20m, assignment.MaxMarks);
        Assert.True(assignment.IsPublished);
    }

    [Fact]
    public async Task Completed_learner_marks_are_immutable_but_other_enrollments_are_not_frozen()
    {
        await using var db = Db();
        var (course, _, enrollment, _) = await Seed(db);
        var assignment = new Assignment
        {
            TenantId = 101, CourseId = course.Id, Title = "Assessment",
            IsPublished = true, MaxMarks = 20
        };
        db.Add(assignment);
        await db.SaveChangesAsync();
        var submission = new AssignmentSubmission
        {
            TenantId = 101, AssignmentId = assignment.Id,
            CourseEnrollmentId = enrollment.Id, RowVersion = [1, 2, 3],
            State = EduOS.Core.Enums.Domain.LearningSubmissionState.Submitted
        };
        db.Add(submission);
        await db.SaveChangesAsync();
        enrollment.State = EduOS.Core.Enums.Domain.CourseEnrollmentState.Completed;
        await db.SaveChangesAsync();

        var result = await Service(db).GradeSubmissionAsync(submission.Id,
            new GradeAssignmentSubmissionRequestDto
            {
                Marks = 18, RowVersion = Convert.ToBase64String(submission.RowVersion)
            });
        Assert.Equal(409, result.StatusCode);
        Assert.Null(submission.Marks);
        Assert.Equal(EduOS.Core.Enums.Domain.LearningSubmissionState.Submitted, submission.State);
    }

    [Fact]
    public async Task Lesson_progress_rejects_ineligible_academic_enrollment()
    {
        await using var db = Db();
        var (course, _, _, lesson) = await Seed(db);
        course.AcademicProgramId = 9001;
        await db.SaveChangesAsync();

        var result = await Service(db, "Student").UpdateLessonProgressAsync(
            new UpdateLessonProgressRequestDto
            {
                LessonReference = lesson.PublicId, ProgressPercent = 100, IsCompleted = true
            });
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(await db.Set<LessonProgress>().ToListAsync());
    }

    [Fact]
    public async Task Completion_register_aggregates_published_evidence_by_enrollment_with_search_and_paging()
    {
        await using var db = Db();
        var (course, _, enrollment, lesson) = await Seed(db);
        var secondStudent = new Student
        {
            TenantId = 101, StudentCode = "S-102", FullName = "Second Learner",
            UserId = 88, StatusCode = "Active"
        };
        db.Add(secondStudent);
        await db.SaveChangesAsync();
        var secondEnrollment = new CourseEnrollment
        {
            TenantId = 101, CourseId = course.Id, StudentId = secondStudent.Id,
            ClientRequestId = Guid.NewGuid(), RowVersion = [4, 5, 6]
        };
        var unpublished = new Lesson
        {
            TenantId = 101, CourseId = course.Id, Title = "Draft", IsPublished = false
        };
        var assignment = new Assignment
        {
            TenantId = 101, CourseId = course.Id, Title = "Published assignment", IsPublished = true
        };
        var quiz = new Quiz
        {
            TenantId = 101, CourseId = course.Id, Title = "Published quiz",
            IsPublished = true, PassMarks = 6
        };
        db.AddRange(secondEnrollment, unpublished, assignment, quiz);
        await db.SaveChangesAsync();
        db.AddRange(
            new LessonProgress
            {
                TenantId = 101, CourseEnrollmentId = enrollment.Id, LessonId = lesson.Id,
                IsCompleted = true, ProgressPercent = 100
            },
            new AssignmentSubmission
            {
                TenantId = 101, CourseEnrollmentId = enrollment.Id, AssignmentId = assignment.Id,
                State = EduOS.Core.Enums.Domain.LearningSubmissionState.Graded, Marks = 8
            },
            new QuizAttempt
            {
                TenantId = 101, CourseEnrollmentId = enrollment.Id, QuizId = quiz.Id,
                AttemptNo = 1, State = EduOS.Core.Enums.Domain.QuizAttemptState.Graded, ObtainedMarks = 5
            },
            new QuizAttempt
            {
                TenantId = 101, CourseEnrollmentId = enrollment.Id, QuizId = quiz.Id,
                AttemptNo = 2, State = EduOS.Core.Enums.Domain.QuizAttemptState.Graded, ObtainedMarks = 7
            },
            new LessonProgress
            {
                TenantId = 202, CourseEnrollmentId = secondEnrollment.Id, LessonId = lesson.Id,
                IsCompleted = true, ProgressPercent = 100
            });
        await TestTenantSeeder.SaveAsync(db);

        var service = Service(db);
        var first = await service.GetCourseCompletionRegisterAsync(course.PublicId, 1, 1, null);
        Assert.True(first.Success, first.Message);
        Assert.Equal(2, first.Data!.TotalCount);
        var firstRow = Assert.Single(first.Data.Items);
        Assert.Equal(enrollment.Id, firstRow.EnrollmentId);
        Assert.True(firstRow.Eligibility.Ready);
        Assert.Equal(1, firstRow.Eligibility.RequiredLessons);
        Assert.Equal(1, firstRow.Eligibility.CompletedLessons);
        Assert.Equal(1, firstRow.Eligibility.RequiredAssignments);
        Assert.Equal(1, firstRow.Eligibility.GradedAssignments);
        Assert.Equal(1, firstRow.Eligibility.RequiredQuizzes);
        Assert.Equal(1, firstRow.Eligibility.PassedQuizzes);

        var second = await service.GetCourseCompletionRegisterAsync(course.PublicId, 2, 1, null);
        Assert.True(second.Success, second.Message);
        var secondRow = Assert.Single(second.Data!.Items);
        Assert.Equal(secondEnrollment.Id, secondRow.EnrollmentId);
        Assert.False(secondRow.Eligibility.Ready);
        Assert.Equal(0, secondRow.Eligibility.CompletedLessons);
        Assert.Equal(0, secondRow.Eligibility.GradedAssignments);
        Assert.Equal(0, secondRow.Eligibility.PassedQuizzes);

        var filtered = await service.GetCourseCompletionRegisterAsync(course.PublicId, 1, 20, "S-102");
        Assert.True(filtered.Success, filtered.Message);
        Assert.Equal(1, filtered.Data!.TotalCount);
        Assert.Equal(secondEnrollment.Id, Assert.Single(filtered.Data.Items).EnrollmentId);
    }

    [Fact]
    public async Task Completion_register_validates_mixed_academic_scope_in_one_bounded_page()
    {
        await using var db = Db();
        var (course, student, enrollment, lesson) = await Seed(db);
        course.AcademicProgramId = 99;
        var year = new AcademicYear
        {
            TenantId = 101, Code = "AY-26", Name = "Academic 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2027, 12, 31)
        };
        var curriculum = new AcademicCurriculum
        {
            TenantId = 101, Code = "CUR-1", Name = "Curriculum", AcademicProgramId = 99,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        db.AddRange(year, curriculum);
        await db.SaveChangesAsync();
        var batch = new AcademicBatch
        {
            TenantId = 101, CampusId = 11, AcademicYearId = year.Id, AcademicProgramId = 99,
            AcademicLevelId = 1, Code = "B-1", Name = "Batch"
        };
        db.Add(batch);
        await db.SaveChangesAsync();
        var academic = new StudentEnrollment
        {
            TenantId = 101, StudentId = student.Id, CampusId = 11,
            AcademicYearId = year.Id, AcademicProgramId = 99,
            AcademicBatchId = batch.Id, AcademicCurriculumId = curriculum.Id,
            EnrollmentDate = new DateOnly(2026, 1, 1), RollNo = "R-1"
        };
        db.Add(academic);
        await db.SaveChangesAsync();
        enrollment.StudentEnrollmentId = academic.Id;
        db.Add(new LessonProgress
        {
            TenantId = 101, CourseEnrollmentId = enrollment.Id, LessonId = lesson.Id,
            IsCompleted = true, ProgressPercent = 100
        });
        var secondStudent = new Student
        {
            TenantId = 101, StudentCode = "S-102", FullName = "Second",
            UserId = 88, StatusCode = "Active"
        };
        var thirdStudent = new Student
        {
            TenantId = 101, StudentCode = "S-103", FullName = "Third",
            UserId = 89, StatusCode = "Active"
        };
        db.AddRange(secondStudent, thirdStudent);
        await db.SaveChangesAsync();
        var second = new CourseEnrollment
        {
            TenantId = 101, CourseId = course.Id, StudentId = secondStudent.Id,
            StudentEnrollmentId = academic.Id, ClientRequestId = Guid.NewGuid()
        };
        var third = new CourseEnrollment
        {
            TenantId = 101, CourseId = course.Id, StudentId = thirdStudent.Id,
            ClientRequestId = Guid.NewGuid()
        };
        db.AddRange(second, third);
        await db.SaveChangesAsync();
        db.AddRange(
            new LessonProgress { TenantId = 101, CourseEnrollmentId = second.Id,
                LessonId = lesson.Id, IsCompleted = true, ProgressPercent = 100 },
            new LessonProgress { TenantId = 101, CourseEnrollmentId = third.Id,
                LessonId = lesson.Id, IsCompleted = true, ProgressPercent = 100 });
        await db.SaveChangesAsync();

        var response = await Service(db).GetCourseCompletionRegisterAsync(course.PublicId, 1, 20, null);
        Assert.True(response.Success, response.Message);
        var rows = response.Data!.Items.ToDictionary(x => x.EnrollmentId);
        Assert.Equal(3, rows.Count);
        Assert.True(rows[enrollment.Id].Eligibility.Ready);
        Assert.False(rows[second.Id].Eligibility.Ready);
        Assert.Contains(rows[second.Id].Eligibility.Blockers, x => x.Contains("Active student academic enrollment"));
        Assert.False(rows[third.Id].Eligibility.Ready);
        Assert.Contains(rows[third.Id].Eligibility.Blockers, x => x.Contains("Academic enrollment is required"));
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
        completionAttempts: new GenericRepository<QuizAttempt>(db),
        submissionYears: new GenericRepository<AcademicYear>(db),
        submissionBatches: new GenericRepository<AcademicBatch>(db),
        submissionCurricula: new GenericRepository<AcademicCurriculum>(db));

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
