namespace EduOS.Core.DTOs.Transport;

/// <summary>Monthly canonical transport assignment events. Small cohorts are suppressed; no student or financial fields.</summary>
public sealed class TransportRiderTurnoverDto
{
    public string FromMonth { get; set; } = string.Empty;
    public string ToMonth { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public IReadOnlyList<TransportRiderTurnoverMonthDto> Months { get; set; } = Array.Empty<TransportRiderTurnoverMonthDto>();
}
public sealed class TransportRiderTurnoverMonthDto
{
    public string Month { get; set; } = string.Empty;
    public int? AssignmentStarts { get; set; }
    public int? AssignmentClosures { get; set; }
}
