using EduOS.Core.DTOs.LMS;
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
public sealed class LmsQuizMultipleChoiceServiceTests
{
    [Fact]
    public async Task Multiple_choice_exact_set_is_graded_and_persisted_atomically_with_idempotent_replay()
    {
        await using var db = Database();
        var seed = await Seed(db);
        var service = Service(db);
        var request = new SubmitQuizAttemptRequestDto { RowVersion = Convert.ToBase64String(seed.Attempt.RowVersion),
            Answers = [new SubmitQuizAnswerRequestDto { QuizQuestionId = seed.Multiple.Id,
                SelectedOptionIds = [seed.CorrectA.Id, seed.CorrectB.Id] }] };
        var result = await service.SubmitQuizAttemptAsync(seed.Attempt.Id, request);
        Assert.True(result.Success, result.Message);
        Assert.Equal(5m, result.Data!.ObtainedMarks);
        var answer = Assert.Single(await db.Set<QuizAnswer>().ToListAsync());
        Assert.Null(answer.SelectedOptionId);
        Assert.Equal(2, await db.Set<QuizAnswerOption>().CountAsync());
        Assert.True((await service.SubmitQuizAttemptAsync(seed.Attempt.Id, request)).Success);
        Assert.Equal(2, await db.Set<QuizAnswerOption>().CountAsync());
        request.Answers = [new SubmitQuizAnswerRequestDto { QuizQuestionId = seed.Multiple.Id,
            SelectedOptionIds = [seed.CorrectA.Id] }];
        Assert.Equal(409, (await service.SubmitQuizAttemptAsync(seed.Attempt.Id, request)).StatusCode);
        var review = await service.GetMyAttemptReviewAsync(seed.Attempt.Id);
        Assert.True(review.Success);
        Assert.Equal(2, review.Data!.Answers.Single(x => x.QuizQuestionId == seed.Multiple.Id).SelectedOptions.Count);
    }

    [Fact]
    public async Task Incomplete_multiple_choice_selection_earns_zero_without_partial_credit()
    {
        await using var db = Database();
        var seed = await Seed(db);
        var result = await Service(db).SubmitQuizAttemptAsync(seed.Attempt.Id, new SubmitQuizAttemptRequestDto
        {
            RowVersion = Convert.ToBase64String(seed.Attempt.RowVersion),
            Answers = [new SubmitQuizAnswerRequestDto { QuizQuestionId = seed.Multiple.Id,
                SelectedOptionIds = [seed.CorrectA.Id] }]
        });
        Assert.True(result.Success, result.Message);
        Assert.Equal(0m, result.Data!.ObtainedMarks);
        Assert.Single(await db.Set<QuizAnswerOption>().ToListAsync());
    }

    [Fact]
    public async Task Duplicate_or_foreign_question_options_are_rejected_before_any_write()
    {
        await using var db = Database();
        var seed = await Seed(db);
        var service = Service(db);
        foreach (var ids in new[] { new long[] { seed.CorrectA.Id, seed.CorrectA.Id },
            new long[] { seed.CorrectA.Id, seed.SingleOption.Id } })
        {
            var response = await service.SubmitQuizAttemptAsync(seed.Attempt.Id, new SubmitQuizAttemptRequestDto
            {
                RowVersion = Convert.ToBase64String(seed.Attempt.RowVersion),
                Answers = [new SubmitQuizAnswerRequestDto { QuizQuestionId = seed.Multiple.Id, SelectedOptionIds = ids }]
            });
            Assert.Equal(400, response.StatusCode);
        }
        Assert.Empty(await db.Set<QuizAnswer>().ToListAsync());
        Assert.Empty(await db.Set<QuizAnswerOption>().ToListAsync());
        Assert.Equal(QuizAttemptState.Started, (await db.Set<QuizAttempt>().SingleAsync()).State);
    }

    [Fact]
    public async Task Single_choice_submission_and_review_use_canonical_option_ids()
    {
        await using var db = Database();
        var seed = await Seed(db);
        var service = Service(db);
        var response = await service.SubmitQuizAttemptAsync(seed.Attempt.Id, new SubmitQuizAttemptRequestDto
        {
            RowVersion = Convert.ToBase64String(seed.Attempt.RowVersion),
            Answers = [new SubmitQuizAnswerRequestDto { QuizQuestionId = seed.Single.Id,
                SelectedOptionIds = [seed.SingleOption.Id] }]
        });
        Assert.True(response.Success, response.Message);
        Assert.Equal(3m, response.Data!.ObtainedMarks);
        Assert.Equal(seed.SingleOption.Id, (await db.Set<QuizAnswer>().SingleAsync()).SelectedOptionId);
        Assert.Empty(await db.Set<QuizAnswerOption>().ToListAsync());
        var review = await service.GetMyAttemptReviewAsync(seed.Attempt.Id);
        Assert.True(review.Success);
        Assert.Single(review.Data!.Answers.Single(x => x.QuizQuestionId == seed.Single.Id).SelectedOptions);
        Assert.Equal(403, (await Service(db, 99).GetMyAttemptReviewAsync(seed.Attempt.Id)).StatusCode);
    }

    [Fact]
    public async Task Single_choice_rejects_more_than_one_option_before_persisting()
    {
        await using var db = Database();
        var seed = await Seed(db);
        var alternate = new QuizOption { TenantId = 101, QuizQuestionId = seed.Single.Id,
            OptionText = "No", IsCorrect = false };
        db.Add(alternate);
        await db.SaveChangesAsync();
        var response = await Service(db).SubmitQuizAttemptAsync(seed.Attempt.Id, new SubmitQuizAttemptRequestDto
        {
            RowVersion = Convert.ToBase64String(seed.Attempt.RowVersion),
            Answers = [new SubmitQuizAnswerRequestDto { QuizQuestionId = seed.Single.Id,
                SelectedOptionIds = [seed.SingleOption.Id, alternate.Id] }]
        });
        Assert.Equal(400, response.StatusCode);
        Assert.Empty(await db.Set<QuizAnswer>().ToListAsync());
        Assert.Empty(await db.Set<QuizAnswerOption>().ToListAsync());
        Assert.Equal(QuizAttemptState.Started, (await db.Set<QuizAttempt>().SingleAsync()).State);
    }

    [Fact]
    public async Task Started_attempt_review_is_not_available()
    {
        await using var db = Database();
        var seed = await Seed(db);
        Assert.Equal(409, (await Service(db).GetMyAttemptReviewAsync(seed.Attempt.Id)).StatusCode);
    }

    private static async Task<(QuizAttempt Attempt, QuizQuestion Multiple, QuizQuestion Single,
        QuizOption CorrectA, QuizOption CorrectB, QuizOption SingleOption)> Seed(EduOSDbContext db)
    {
        var student = new Student { TenantId = 101, PersonId = 1, UserId = 51,
            StudentCode = "Q51", FullName = "Learner", StatusCode = "Active" };
        var course = new Course { TenantId = 101, Code = "QZ", Title = "Quiz course", IsActive = true };
        db.AddRange(student, course); await db.SaveChangesAsync();
        var enrollment = new CourseEnrollment { TenantId = 101, CourseId = course.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active };
        var quiz = new Quiz { TenantId = 101, CourseId = course.Id, Title = "Quiz",
            IsPublished = true, DurationMinutes = 30, MaxAttempts = 2 };
        db.AddRange(enrollment, quiz); await db.SaveChangesAsync();
        var multiple = new QuizQuestion { TenantId = 101, QuizId = quiz.Id, QuestionText = "Choose both",
            QuestionType = "MultipleChoice", Marks = 5m, DisplayOrder = 1 };
        var single = new QuizQuestion { TenantId = 101, QuizId = quiz.Id, QuestionText = "Choose one",
            QuestionType = "SingleChoice", Marks = 3m, DisplayOrder = 2 };
        var attempt = new QuizAttempt { TenantId = 101, QuizId = quiz.Id, CourseEnrollmentId = enrollment.Id,
            ClientRequestId = Guid.NewGuid(), StartedAt = DateTime.UtcNow, State = QuizAttemptState.Started,
            RowVersion = [1, 2, 3] };
        db.AddRange(multiple, single, attempt); await db.SaveChangesAsync();
        var a = new QuizOption { TenantId = 101, QuizQuestionId = multiple.Id, OptionText = "A", IsCorrect = true };
        var b = new QuizOption { TenantId = 101, QuizQuestionId = multiple.Id, OptionText = "B", IsCorrect = true };
        var c = new QuizOption { TenantId = 101, QuizQuestionId = multiple.Id, OptionText = "C", IsCorrect = false };
        var one = new QuizOption { TenantId = 101, QuizQuestionId = single.Id, OptionText = "Yes", IsCorrect = true };
        db.AddRange(a, b, c, one); await db.SaveChangesAsync();
        return (attempt, multiple, single, a, b, one);
    }

    private static EduOSDbContext Database()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-multi-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LmsAssessmentService Service(EduOSDbContext db, long userId = 51) => new(
        new GenericRepository<Course>(db), new GenericRepository<Quiz>(db),
        new GenericRepository<QuizQuestion>(db), new GenericRepository<QuizOption>(db),
        new GenericRepository<QuizAttempt>(db), new GenericRepository<QuizAnswer>(db),
        new GenericRepository<CourseEnrollment>(db), new GenericRepository<Student>(db),
        new GenericRepository<Employee>(db), db, new StudentUser(userId), TimeProvider.System,
        NullLogger<LmsAssessmentService>.Instance, new GenericRepository<QuizAnswerOption>(db));

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
