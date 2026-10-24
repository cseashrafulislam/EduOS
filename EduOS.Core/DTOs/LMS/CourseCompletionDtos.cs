using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.LMS;

public sealed class CourseCompletionEligibilityDto
{
    public int RequiredLessons { get; set; }
    public int CompletedLessons { get; set; }
    public int RequiredAssignments { get; set; }
    public int GradedAssignments { get; set; }
    public int RequiredQuizzes { get; set; }
    public int PassedQuizzes { get; set; }
    public bool Ready { get; set; }
    public IReadOnlyList<string> Blockers { get; set; } = Array.Empty<string>();
}

public sealed class CourseCompletionRegisterRowDto
{
    public long EnrollmentId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public CourseEnrollmentState State { get; set; }
    public DateTime EnrolledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public CourseCompletionEligibilityDto Eligibility { get; set; } = new();
}

public sealed class CompleteCourseEnrollmentRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
