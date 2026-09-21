using EduOS.Core.Entities.Academic;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

public sealed class AcademicCalendarPolicyDto
{
    public long Id { get; set; }
    public long AcademicYearId { get; set; }
    public long? CampusId { get; set; }
    public IReadOnlyList<DayOfWeek> WeekendDays { get; set; } = [];
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveAcademicCalendarPolicyDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicYearId { get; set; }
    public long? CampusId { get; set; }
    [MinLength(1), MaxLength(6)] public List<DayOfWeek> WeekendDays { get; set; } = [];
    public string? RowVersion { get; set; }
}

public sealed class AcademicCalendarEventDto
{
    public long Id { get; set; }
    public long? CampusId { get; set; }
    public long? AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public AcademicCalendarEventType EventType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Location { get; set; }
    public bool IsHoliday { get; set; }
    public bool IsPublicVisible { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateAcademicCalendarEventDto
{
    public Guid ClientRequestId { get; set; }
    public long? CampusId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public AcademicCalendarEventType EventType { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(1000)] public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [StringLength(300)] public string? Location { get; set; }
    public bool IsHoliday { get; set; }
    public bool IsPublicVisible { get; set; } = true;
}

public sealed class UpdateAcademicCalendarEventDto
{
    public AcademicCalendarEventType EventType { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(1000)] public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [StringLength(300)] public string? Location { get; set; }
    public bool IsHoliday { get; set; }
    public bool IsPublicVisible { get; set; } = true;
    public bool IsActive { get; set; } = true;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class AcademicWorkingDayDto
{
    public DateTime Date { get; set; }
    public bool IsWorkingDay { get; set; }
    public bool IsWeekend { get; set; }
    public IReadOnlyList<string> HolidayNames { get; set; } = [];
}
