namespace EduOS.Core.DTOs.LMS;

/// <summary>Read-only aggregation of canonical LMS DTOs, without duplicate editable fields.</summary>
public sealed class CourseDetailsDto
{
    public CourseDto Course { get; set; } = new();
    public IReadOnlyList<LessonDto> Lessons { get; set; } = Array.Empty<LessonDto>();
    public IReadOnlyList<AssignmentDto> Assignments { get; set; } = Array.Empty<AssignmentDto>();
}
