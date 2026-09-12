using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Attendance;

public class StudentAttendanceRosterQueryDto
{
    public DateTime Date { get; set; } = DateTime.Today;

    [Range(1, int.MaxValue)]
    public int AcademicYearId { get; set; }

    [Range(1, int.MaxValue)]
    public int ClassId { get; set; }

    [Range(1, int.MaxValue)]
    public int SectionId { get; set; }
}

public sealed class SaveStudentAttendanceItemDto
{
    [Range(1, int.MaxValue)]
    public int StudentId { get; set; }

    [Required, RegularExpression("^(Present|Absent|Late|Leave)$")]
    public string Status { get; set; } = "Present";

    public TimeSpan? InTime { get; set; }
    public TimeSpan? OutTime { get; set; }

    [StringLength(500)]
    public string? Remarks { get; set; }
}

public sealed class SaveStudentAttendanceDto : StudentAttendanceRosterQueryDto
{
    [Required, MinLength(1)]
    public List<SaveStudentAttendanceItemDto> Items { get; set; } = new();
}

public sealed class StudentAttendanceRosterItemDto
{
    public int StudentId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string Roll { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? Status { get; set; }
    public TimeSpan? InTime { get; set; }
    public TimeSpan? OutTime { get; set; }
    public string? Remarks { get; set; }
}

public sealed class StudentAttendanceSummaryDto
{
    public int TotalStudents { get; set; }
    public int Marked { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int Leave { get; set; }
    public int Unmarked { get; set; }
}

public sealed class StudentAttendanceRosterDto
{
    public DateTime Date { get; set; }
    public int AcademicYearId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public StudentAttendanceSummaryDto Summary { get; set; } = new();
    public List<StudentAttendanceRosterItemDto> Students { get; set; } = new();
}
