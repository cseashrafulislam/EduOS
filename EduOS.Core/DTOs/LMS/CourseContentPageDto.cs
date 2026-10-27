using EduOS.Core.Common;
namespace EduOS.Core.DTOs.LMS;
public sealed class CourseContentPageDto
{
    public CourseDto Course { get; set; } = new();
    public PagedResult<LessonDto> Lessons { get; set; } = new();
    public PagedResult<AssignmentDto> Assignments { get; set; } = new();
}
