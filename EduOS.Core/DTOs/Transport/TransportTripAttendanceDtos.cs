using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;
public sealed class RecordTransportTripAttendanceRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(60)] public string StudentCode { get; set; } = string.Empty;
    [Required, MaxLength(12)] public string EventType { get; set; } = string.Empty;
}
public sealed class TransportTripAttendanceDto
{
    public string StudentCode { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}
public sealed class TransportFamilyAttendanceDto
{
    public Guid TripReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public DateOnly OperatingDate { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string AttendanceState { get; set; } = string.Empty;
    public DateTime? LastEventAt { get; set; }
}

public sealed class CorrectTransportTripAttendanceRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(60)] public string StudentCode { get; set; } = string.Empty;
    [Required, MaxLength(12)] public string ExpectedState { get; set; } = string.Empty;
    [Required, MaxLength(12)] public string CorrectedState { get; set; } = string.Empty;
    [Required, MinLength(15), MaxLength(500)] public string Reason { get; set; } = string.Empty;
}
public sealed class TransportTripAttendanceHistoryDto
{
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public bool IsCorrection { get; set; }
    public string? CorrectionReason { get; set; }
}
