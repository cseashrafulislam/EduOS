using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

public sealed class RoutineTimeSlotDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsBreak { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateRoutineTimeSlotDto
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsBreak { get; set; }
}

public sealed class InstructorAssignmentDto
{
    public long Id { get; set; }
    public long AcademicBatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public long EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsClassAdvisor { get; set; }
    public bool IsActive { get; set; }
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

public sealed class RoutineEntryDto
{
    public long Id { get; set; }
    public long AcademicBatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public long EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public long RoutineTimeSlotId { get; set; }
    public string TimeSlotName { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public long? RoomId { get; set; }
    public string? RoomName { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateRoutineEntryDto
{
    [Range(1, long.MaxValue)] public long InstructorAssignmentId { get; set; }
    [Range(1, long.MaxValue)] public long RoutineTimeSlotId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public long? RoomId { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
}
