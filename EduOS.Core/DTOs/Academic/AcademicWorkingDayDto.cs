namespace EduOS.Core.DTOs.Academic;

/// <summary>Calendar business-date projection, calculated from campus/year policy and published events.</summary>
public sealed class AcademicWorkingDayDto
{
    public DateOnly Date { get; set; }
    public bool IsWorkingDay { get; set; }
    public bool IsWeekend { get; set; }
    public IReadOnlyList<string> HolidayNames { get; set; } = Array.Empty<string>();
}
