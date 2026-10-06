namespace EduOS.Core.DTOs.LMS;

public class CourseDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long? AcademicProgramId { get; set; }
    public string? AcademicProgramName { get; set; }
    public long? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public Guid? PrimaryInstructorReference { get; set; }
    public string? PrimaryInstructorName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool IsSelfPaced { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveCourseRequestDto
{
    public long? AcademicProgramId { get; set; }
    public long? SubjectId { get; set; }
    public Guid? PrimaryInstructorReference { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Description { get; set; }
    [MaxLength(500)] public string? ThumbnailUrl { get; set; }
    public bool IsSelfPaced { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class CourseEnrollmentDto
{
    public long Id { get; set; }
    public Guid CourseReference { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public Guid? StudentEnrollmentReference { get; set; }
    public CourseEnrollmentState State { get; set; }
    public DateTime EnrolledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal ProgressPercent { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class EnrollCourseRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid CourseReference { get; set; }
    public Guid StudentReference { get; set; }
    public Guid? StudentEnrollmentReference { get; set; }
}

public class ChangeCourseEnrollmentStateRequestDto
{
    public CourseEnrollmentState State { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class LessonDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid CourseReference { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? ContentUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveLessonRequestDto
{
    public Guid CourseReference { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Content { get; set; }
    [MaxLength(500)] public string? ContentUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public string? RowVersion { get; set; }
}

public class LessonProgressDto
{
    public long Id { get; set; }
    public long CourseEnrollmentId { get; set; }
    public Guid LessonReference { get; set; }
    public string LessonTitle { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal ProgressPercent { get; set; }
}

public class UpdateLessonProgressRequestDto
{
    public Guid LessonReference { get; set; }
    [Range(typeof(decimal), "0", "100")] public decimal ProgressPercent { get; set; }
    public bool IsCompleted { get; set; }
}

public class AssignmentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid CourseReference { get; set; }
    public LearningTaskType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? DueAt { get; set; }
    public decimal MaxMarks { get; set; }
    public bool IsPublished { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAssignmentRequestDto
{
    public Guid CourseReference { get; set; }
    public LearningTaskType Type { get; set; } = LearningTaskType.Assignment;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Instructions { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? DueAt { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal MaxMarks { get; set; }
    public bool IsPublished { get; set; }
    public string? RowVersion { get; set; }
}

public class AssignmentSubmissionDto
{
    public long Id { get; set; }
    public Guid AssignmentReference { get; set; }
    public long CourseEnrollmentId { get; set; }
    public string? SubmissionText { get; set; }
    public long? FileAssetId { get; set; }
    public LearningSubmissionState State { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public decimal? Marks { get; set; }
    public string? Feedback { get; set; }
    public long? GradedByUserId { get; set; }
    public DateTime? GradedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SubmitAssignmentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid AssignmentReference { get; set; }
    public long CourseEnrollmentId { get; set; }
    [MaxLength(4000)] public string? SubmissionText { get; set; }
    public long? FileAssetId { get; set; }
}

public class GradeAssignmentSubmissionRequestDto
{
    [Range(typeof(decimal), "0", "100000")] public decimal Marks { get; set; }
    [MaxLength(2000)] public string? Feedback { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class QuizDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid CourseReference { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public decimal PassMarks { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public bool IsPublished { get; set; }
    public IReadOnlyList<QuizQuestionDto> Questions { get; set; } = Array.Empty<QuizQuestionDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveQuizRequestDto
{
    public Guid CourseReference { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Range(1, 1440)] public int DurationMinutes { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal PassMarks { get; set; }
    [Range(1, 100)] public int MaxAttempts { get; set; } = 1;
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public bool IsPublished { get; set; }
    public IReadOnlyList<SaveQuizQuestionRequestDto> Questions { get; set; } = Array.Empty<SaveQuizQuestionRequestDto>();
    public string? RowVersion { get; set; }
}

public class QuizQuestionDto
{
    public long Id { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = string.Empty;
    public decimal Marks { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyList<QuizOptionDto> Options { get; set; } = Array.Empty<QuizOptionDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveQuizQuestionRequestDto
{
    public long? Id { get; set; }
    [Required, MaxLength(4000)] public string QuestionText { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string QuestionType { get; set; } = "SingleChoice";
    [Range(typeof(decimal), "0", "100000")] public decimal Marks { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyList<SaveQuizOptionRequestDto> Options { get; set; } = Array.Empty<SaveQuizOptionRequestDto>();
    public string? RowVersion { get; set; }
}

public class QuizOptionDto
{
    public long Id { get; set; }
    public string OptionText { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int DisplayOrder { get; set; }
}

public class SaveQuizOptionRequestDto
{
    public long? Id { get; set; }
    [Required, MaxLength(2000)] public string OptionText { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int DisplayOrder { get; set; }
}

public class QuizAttemptDto
{
    public long Id { get; set; }
    public Guid QuizReference { get; set; }
    public long CourseEnrollmentId { get; set; }
    public int AttemptNo { get; set; }
    public QuizAttemptState State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public decimal? ObtainedMarks { get; set; }
}

public class StartQuizAttemptRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid QuizReference { get; set; }
    public long CourseEnrollmentId { get; set; }
}

public class SubmitQuizAttemptRequestDto
{
    public IReadOnlyList<SubmitQuizAnswerRequestDto> Answers { get; set; } = Array.Empty<SubmitQuizAnswerRequestDto>();
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class SubmitQuizAnswerRequestDto
{
    public long QuizQuestionId { get; set; }
    public long? SelectedOptionId { get; set; }
    [MaxLength(4000)] public string? AnswerText { get; set; }
}

public class LiveClassDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid CourseReference { get; set; }
    public Guid InstructorReference { get; set; }
    public string InstructorName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string? JoinUrl { get; set; }
    public string? RecordingUrl { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveLiveClassRequestDto
{
    public Guid CourseReference { get; set; }
    public Guid InstructorReference { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    [MaxLength(1000)] public string? JoinUrl { get; set; }
    [MaxLength(1000)] public string? RecordingUrl { get; set; }
    public string? RowVersion { get; set; }
}
