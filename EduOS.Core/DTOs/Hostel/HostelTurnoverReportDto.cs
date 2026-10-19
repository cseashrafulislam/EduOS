namespace EduOS.Core.DTOs.Hostel;

/// <summary>Monthly canonical allocation events. Small cohorts are suppressed; no student or financial fields.</summary>
public sealed class HostelTurnoverReportDto
{
    public string FromMonth { get; set; } = string.Empty;
    public string ToMonth { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public IReadOnlyList<HostelTurnoverMonthDto> Months { get; set; } = Array.Empty<HostelTurnoverMonthDto>();
}
public sealed class HostelTurnoverMonthDto
{
    public string Month { get; set; } = string.Empty;
    public int? AllocationStarts { get; set; }
    public int? AllocationClosures { get; set; }
}
