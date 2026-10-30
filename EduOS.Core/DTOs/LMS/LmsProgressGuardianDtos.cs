using System.ComponentModel.DataAnnotations;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.LMS;

public sealed class LmsProgressGuardianGrantDto
{
    public Guid Reference { get; set; }
    public long CourseEnrollmentId { get; set; }
    public long GuardianLinkId { get; set; }
    public string GuardianName { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class GrantLmsProgressGuardianRequestDto
{
    public long CourseEnrollmentId { get; set; }
    public long GuardianLinkId { get; set; }
    public Guid ClientRequestId { get; set; }
}
public sealed class RevokeLmsProgressGuardianRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
public sealed class LmsGuardianCourseProgressDto
{
    public long CourseEnrollmentId { get; set; }
    public Guid CourseReference { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string LearnerName { get; set; } = string.Empty;
    public CourseEnrollmentState State { get; set; }
    public int PublishedLessons { get; set; }
    public int CompletedLessons { get; set; }
    public decimal LessonCompletionPercent { get; set; }
}
