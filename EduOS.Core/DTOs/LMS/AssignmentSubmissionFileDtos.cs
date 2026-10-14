namespace EduOS.Core.DTOs.LMS;

/// <summary>Student-only assignment workspace; exposes no private storage keys or other learners' data.</summary>
public sealed class MyAssignmentWorkDto
{
    public Guid AssignmentReference { get; set; }
    public string AssignmentTitle { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public long CourseEnrollmentId { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? DueAt { get; set; }
    public decimal MaxMarks { get; set; }
    public long? SubmissionId { get; set; }
    public LearningSubmissionState? State { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public decimal? Marks { get; set; }
    public string? Feedback { get; set; }
    public bool CanSubmit { get; set; }
    public string? EligibilityMessage { get; set; }
}
public sealed class AssignmentSubmissionFileDto
{
    public long Id { get; set; }
    public Guid FileAssetReference { get; set; }
    public string FileName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}
