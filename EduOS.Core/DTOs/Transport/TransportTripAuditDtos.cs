namespace EduOS.Core.DTOs.Transport;

/// <summary>One immutable attendance event, including its audited correction lineage.</summary>
public sealed class TransportTripAuditRowDto
{
    public string StudentCode { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? CorrectedFrom { get; set; }
    public string? CorrectionReason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long? RecordedByUserId { get; set; }
    public bool IsCorrection { get; set; }
}

public sealed class TransportTripAuditReportDto
{
    public TransportTripRowDto Trip { get; set; } = new();
    public IReadOnlyList<TransportTripAuditRowDto> Events { get; set; } =
        Array.Empty<TransportTripAuditRowDto>();
    public DateTime GeneratedAtUtc { get; set; }
}
