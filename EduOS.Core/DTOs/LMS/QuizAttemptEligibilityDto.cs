namespace EduOS.Core.DTOs.LMS;

/// <summary>Student-owned, read-only quiz access status; writes always recheck eligibility.</summary>
public sealed class QuizAttemptEligibilityDto
{
    public Guid CourseReference { get; set; }
    public bool CanAttempt { get; set; }
    public long CourseEnrollmentId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
