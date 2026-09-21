using EduOS.Core.Entities.Academic;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

public sealed class RoutineSubstitutionDto
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public long RoutineEntryId { get; set; }
    public long AcademicBatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public long OriginalTeacherId { get; set; }
    public string OriginalTeacherName { get; set; } = string.Empty;
    public long SubstituteTeacherId { get; set; }
    public string SubstituteTeacherName { get; set; } = string.Empty;
    public long RoutineTimeSlotId { get; set; }
    public string TimeSlotName { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateRoutineSubstitutionDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long RoutineEntryId { get; set; }
    public DateTime Date { get; set; }
    [Range(1, long.MaxValue)] public long SubstituteTeacherId { get; set; }
    [StringLength(1000)] public string? Reason { get; set; }
}

public sealed class CancelRoutineSubstitutionDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, StringLength(1000)] public string Reason { get; set; } = string.Empty;
}

public sealed class LessonPlanDto
{
    public long Id { get; set; }
    public long InstructorAssignmentId { get; set; }
    public long AcademicBatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public long TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public string ChapterName { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Description { get; set; }
    public string? LearningObjectives { get; set; }
    public string? Resources { get; set; }
    public string? ProgressNotes { get; set; }
    public LessonPlanStatus Status { get; set; }
    public int ProgressPercent { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public long? ReviewedBy { get; set; }
    public string? ReviewRemarks { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateLessonPlanDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long InstructorAssignmentId { get; set; }
    [Required, StringLength(500)] public string ChapterName { get; set; } = string.Empty;
    [StringLength(500)] public string? Topic { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    [StringLength(2000)] public string? LearningObjectives { get; set; }
    [StringLength(2000)] public string? Resources { get; set; }
}

public sealed class UpdateLessonPlanDto
{
    [Required, StringLength(500)] public string ChapterName { get; set; } = string.Empty;
    [StringLength(500)] public string? Topic { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    [StringLength(2000)] public string? LearningObjectives { get; set; }
    [StringLength(2000)] public string? Resources { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LessonPlanReviewDto
{
    public bool Approve { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LessonPlanProgressDto
{
    [Range(0, 100)] public int ProgressPercent { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class AcademicRowVersionDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
