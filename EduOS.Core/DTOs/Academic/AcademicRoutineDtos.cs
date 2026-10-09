using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

public sealed class CreateRoutineTimeSlotDto
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsBreak { get; set; }
}

public sealed class AssignInstructorDto
{
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    [Range(1, long.MaxValue)] public long SubjectId { get; set; }
    [Range(1, long.MaxValue)] public long EmployeeId { get; set; }
    public long? AcademicTermId { get; set; }
    public bool IsPrimary { get; set; } = true;
    public bool IsClassAdvisor { get; set; }
}

public sealed class CreateRoutineEntryDto
{
    [Range(1, long.MaxValue)] public long InstructorAssignmentId { get; set; }
    [Range(1, long.MaxValue)] public long RoutineTimeSlotId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public long? RoomId { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
}

public sealed class AcademicInstructorChoiceDto
{
    public long Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
