using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsAssessmentService : ILmsAssessmentService
{
    private readonly IGenericRepository<Course> _courses;
    private readonly IGenericRepository<Quiz> _quizzes;
    private readonly IGenericRepository<QuizQuestion> _questions;
    private readonly IGenericRepository<QuizOption> _options;
    private readonly IGenericRepository<QuizAttempt> _attempts;
    private readonly IGenericRepository<QuizAnswer> _answers;
    private readonly IGenericRepository<QuizAnswerOption> _answerOptions;
    private readonly IGenericRepository<CourseEnrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<StudentEnrollment>? _historyAcademics;
    private readonly IGenericRepository<AcademicBatch>? _historyBatches;
    private readonly IGenericRepository<AcademicYear>? _historyYears;
    private readonly IGenericRepository<AcademicCurriculum>? _historyCurricula;
    private readonly IGenericRepository<AcademicTerm>? _historyTerms;
    private readonly IGenericRepository<CurriculumSubject>? _historySubjects;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<LmsAssessmentService> _logger;

    public LmsAssessmentService(IGenericRepository<Course> courses, IGenericRepository<Quiz> quizzes,
        IGenericRepository<QuizQuestion> questions, IGenericRepository<QuizOption> options,
        IGenericRepository<QuizAttempt> attempts, IGenericRepository<QuizAnswer> answers,
        IGenericRepository<CourseEnrollment> enrollments, IGenericRepository<Student> students,
        IGenericRepository<Employee> employees, IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<LmsAssessmentService> logger,
        IGenericRepository<QuizAnswerOption> answerOptions,
        IGenericRepository<StudentEnrollment>? historyAcademics = null,
        IGenericRepository<AcademicBatch>? historyBatches = null,
        IGenericRepository<AcademicYear>? historyYears = null,
        IGenericRepository<AcademicCurriculum>? historyCurricula = null,
        IGenericRepository<AcademicTerm>? historyTerms = null,
        IGenericRepository<CurriculumSubject>? historySubjects = null)
    {
        _courses = courses; _quizzes = quizzes; _questions = questions; _options = options;
        _attempts = attempts; _answers = answers; _answerOptions = answerOptions; _enrollments = enrollments;
        _students = students; _employees = employees; _uow = uow;
        _user = user; _clock = clock; _logger = logger;
        _historyAcademics = historyAcademics; _historyBatches = historyBatches;
        _historyYears = historyYears; _historyCurricula = historyCurricula;
        _historyTerms = historyTerms; _historySubjects = historySubjects;
    }

    private bool Authenticated => _user.IsAuthenticated && _user.TenantId > 0;
    private bool Manager => _user.IsTenantAdmin || _user.IsInRole("Principal");
    private bool Author => Authenticated && (Manager || _user.IsInRole("Teacher"));
    private bool Learner => Authenticated && _user.IsInRole("Student");
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private static bool VersionMatches(byte[] actual, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var expected = Convert.FromBase64String(value);
            return actual.Length > 0 && expected.Length == actual.Length &&
                CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    private async Task<Course?> AuthorCourseAsync(Guid reference, CancellationToken ct)
    {
        if (!Author || reference == Guid.Empty) return null;
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (course == null) return null;
        if (Manager) return course;
        var employeeId = await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.UserId == _user.UserId && x.CanTeach &&
            x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
        return employeeId > 0 && course.PrimaryInstructorEmployeeId == employeeId ? course : null;
    }

    private async Task<long> StudentIdAsync(CancellationToken ct)
    {
        if (!Learner) return 0;
        return await _students.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.UserId == _user.UserId &&
            x.StatusCode == "Active" && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<CourseEnrollment?> EnrollmentAsync(long courseId, long enrollmentId, CancellationToken ct)
    {
        var studentId = await StudentIdAsync(ct);
        if (studentId == 0) return null;
        return await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == enrollmentId && x.CourseId == courseId &&
            x.StudentId == studentId && !x.IsDeleted &&
            (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed), ct);
    }

    private async Task<QuizDto> MapQuizAsync(Quiz quiz, CancellationToken ct)
    {
        var courseReference = await _courses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == quiz.TenantId && x.Id == quiz.CourseId)
            .Select(x => x.PublicId).FirstAsync(ct);
        var questions = await _questions.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == quiz.TenantId && x.QuizId == quiz.Id && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var ids = questions.Select(x => x.Id).ToArray();
        var options = await _options.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == quiz.TenantId && ids.Contains(x.QuizQuestionId) && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(ct);
        return new QuizDto
        {
            Id = quiz.Id, Reference = quiz.PublicId, CourseReference = courseReference,
            Title = quiz.Title, DurationMinutes = quiz.DurationMinutes, PassMarks = quiz.PassMarks,
            MaxAttempts = quiz.MaxAttempts, OpensAt = quiz.OpensAt, ClosesAt = quiz.ClosesAt,
            IsPublished = quiz.IsPublished, RowVersion = Convert.ToBase64String(quiz.RowVersion),
            Questions = questions.Select(q => new QuizQuestionDto
            {
                Id = q.Id, QuestionText = q.QuestionText, QuestionType = q.QuestionType,
                Marks = q.Marks, DisplayOrder = q.DisplayOrder,
                RowVersion = Convert.ToBase64String(q.RowVersion),
                Options = options.Where(o => o.QuizQuestionId == q.Id).Select(o => new QuizOptionDto
                {
                    Id = o.Id, OptionText = o.OptionText, IsCorrect = o.IsCorrect, DisplayOrder = o.DisplayOrder
                }).ToList()
            }).ToList()
        };
    }

    private static QuizAttemptDto MapAttempt(QuizAttempt attempt, Guid quizReference) => new()
    {
        Id = attempt.Id, QuizReference = quizReference, CourseEnrollmentId = attempt.CourseEnrollmentId,
        AttemptNo = attempt.AttemptNo, State = attempt.State, StartedAt = attempt.StartedAt,
        SubmittedAt = attempt.SubmittedAt, ObtainedMarks = attempt.ObtainedMarks,
        RowVersion = Convert.ToBase64String(attempt.RowVersion)
    };
}
