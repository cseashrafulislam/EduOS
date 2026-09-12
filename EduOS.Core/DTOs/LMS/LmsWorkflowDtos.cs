using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.LMS;

public sealed class SaveCourseDto
{
    public Guid? Reference { get; set; }
    [Range(1,long.MaxValue)] public long AcademicYearId { get; set; }
    [Range(1,long.MaxValue)] public long ClassId { get; set; }
    [Range(1,long.MaxValue)] public long? SectionId { get; set; }
    [Range(1,long.MaxValue)] public long SubjectId { get; set; }
    [Range(1,long.MaxValue)] public long? TeacherId { get; set; }
    [Required,StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(4000)] public string? Description { get; set; }
    [StringLength(1000)] public string? ThumbnailUrl { get; set; }
}
public sealed class SaveLessonDto
{
    public Guid CourseReference { get; set; }
    public Guid? Reference { get; set; }
    [Required,StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(20000)] public string? Content { get; set; }
    [StringLength(1000)] public string? VideoUrl { get; set; }
    [StringLength(1000)] public string? AttachmentUrl { get; set; }
    [Range(1,10000)] public int OrderNo { get; set; } = 1;
    [Range(0,10000)] public int Duration { get; set; }
}
public sealed class SaveAssignmentDto
{
    public Guid CourseReference { get; set; }
    public Guid? Reference { get; set; }
    [Required,StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(8000)] public string? Description { get; set; }
    [Range(0,100000)] public int TotalMark { get; set; }
    public DateTime DueDate { get; set; }
    [StringLength(1000)] public string? AttachmentUrl { get; set; }
}
public sealed class SubmitAssignmentDto
{
    public Guid AssignmentReference { get; set; }
    public Guid ClientRequestId { get; set; }
    [StringLength(1000)] public string? SubmissionFile { get; set; }
    [StringLength(20000)] public string? SubmissionText { get; set; }
}
public sealed class ReviewSubmissionDto
{
    public Guid SubmissionReference { get; set; }
    [Range(typeof(decimal),"0","100000")] public decimal Mark { get; set; }
    [StringLength(4000)] public string? Feedback { get; set; }
}
public sealed class CompleteLessonDto { public Guid LessonReference { get; set; } }
public sealed class LmsCourseDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long ClassId { get; set; }
    public long? SectionId { get; set; }
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public long TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public decimal ProgressPercentage { get; set; }
}
public sealed class LmsLessonDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? VideoUrl { get; set; }
    public string? AttachmentUrl { get; set; }
    public int OrderNo { get; set; }
    public int Duration { get; set; }
    public bool IsCompleted { get; set; }
}
public sealed class LmsAssignmentDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int TotalMark { get; set; }
    public DateTime DueDate { get; set; }
    public string? AttachmentUrl { get; set; }
    public Guid? SubmissionReference { get; set; }
    public string? SubmissionStatus { get; set; }
    public decimal? Mark { get; set; }
    public string? Feedback { get; set; }
}
public sealed class LmsCourseDetailsDto
{
    public LmsCourseDto Course { get; set; } = new();
    public List<LmsLessonDto> Lessons { get; set; } = new();
    public List<LmsAssignmentDto> Assignments { get; set; } = new();
}
