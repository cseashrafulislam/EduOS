namespace EduOS.Core.DTOs.LMS;
public sealed class CourseAuthoringLockDto
{
    public Guid CourseReference { get; set; }
    public bool IsLocked { get; set; }
    public string? Reason { get; set; }
}
