using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS;

public class Course : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long? AcademicProgramId { get; set; }
    public long? SubjectId { get; set; }
    public long? PrimaryInstructorEmployeeId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Description { get; set; }
    [MaxLength(500)] public string? ThumbnailUrl { get; set; }
    public bool IsSelfPaced { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CourseEnrollment : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long CourseId { get; set; }
    public long StudentId { get; set; }
    public long? StudentEnrollmentId { get; set; }
    public CourseEnrollmentState State { get; set; } = CourseEnrollmentState.Active;
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public class Lesson : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long CourseId { get; set; }
    public long? CourseSectionId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Content { get; set; }
    [MaxLength(500)] public string? ContentUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
}

public class LessonProgress : BaseTenantEntity
{
    public long CourseEnrollmentId { get; set; }
    public long LessonId { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    [Column(TypeName = "decimal(5,2)")] public decimal ProgressPercent { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}

public class Assignment : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long CourseId { get; set; }
    public LearningTaskType Type { get; set; } = LearningTaskType.Assignment;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Instructions { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? DueAt { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MaxMarks { get; set; }
    public bool IsPublished { get; set; }
}

public class AssignmentSubmission : BaseTenantEntity
{
    public long AssignmentId { get; set; }
    public long CourseEnrollmentId { get; set; }
    [MaxLength(4000)] public string? SubmissionText { get; set; }
    public long? FileAssetId { get; set; }
    public LearningSubmissionState State { get; set; } = LearningSubmissionState.Draft;
    public DateTime? SubmittedAt { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Marks { get; set; }
    [MaxLength(2000)] public string? Feedback { get; set; }
    public long? GradedByUserId { get; set; }
    public DateTime? GradedAt { get; set; }
}

public class Quiz : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long CourseId { get; set; }
    public long? PreviousVersionId { get; set; }
    public int VersionNo { get; set; } = 1;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PassMarks { get; set; }
    public int MaxAttempts { get; set; } = 1;
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public bool IsPublished { get; set; }
}

public class QuizQuestion : BaseTenantEntity
{
    public long QuizId { get; set; }
    [Required, MaxLength(4000)] public string QuestionText { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string QuestionType { get; set; } = "SingleChoice";
    [Column(TypeName = "decimal(18,2)")] public decimal Marks { get; set; }
    public int DisplayOrder { get; set; }
}

public class QuizOption : BaseTenantEntity
{
    public long QuizQuestionId { get; set; }
    [Required, MaxLength(2000)] public string OptionText { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int DisplayOrder { get; set; }
}

public class QuizAttempt : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long QuizId { get; set; }
    public long CourseEnrollmentId { get; set; }
    public int AttemptNo { get; set; }
    public QuizAttemptState State { get; set; } = QuizAttemptState.Started;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ObtainedMarks { get; set; }
}

public class QuizAnswer : BaseTenantEntity
{
    public long QuizAttemptId { get; set; }
    public long QuizQuestionId { get; set; }
    public long? SelectedOptionId { get; set; }
    [MaxLength(4000)] public string? AnswerText { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? AwardedMarks { get; set; }
}

public class LiveClass : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long CourseId { get; set; }
    public long InstructorEmployeeId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    [MaxLength(100)] public string? ProviderCode { get; set; }
    [MaxLength(200)] public string? ExternalMeetingId { get; set; }
    [MaxLength(1000)] public string? JoinUrl { get; set; }
    [MaxLength(1000)] public string? RecordingUrl { get; set; }
}

public class CourseSection : BaseTenantEntity
{
    public long CourseId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
}

public class LessonResource : BaseTenantEntity
{
    public long LessonId { get; set; }
    public long? FileAssetId { get; set; }
    [MaxLength(1000)] public string? ExternalUrl { get; set; }
    [MaxLength(200)] public string? Title { get; set; }
    public int DisplayOrder { get; set; }
}

public class AssignmentAttachment : BaseTenantEntity
{
    public long AssignmentId { get; set; }
    public long FileAssetId { get; set; }
    [MaxLength(200)] public string? Title { get; set; }
}

public class AssignmentSubmissionFile : BaseTenantEntity
{
    public long AssignmentSubmissionId { get; set; }
    public long FileAssetId { get; set; }
}

public class QuizAnswerOption : BaseTenantEntity
{
    public long QuizAnswerId { get; set; }
    public long QuizOptionId { get; set; }
}
