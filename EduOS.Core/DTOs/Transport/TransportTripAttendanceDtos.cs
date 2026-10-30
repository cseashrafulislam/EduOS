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

// Atomic, explicitly confirmed closure of remaining unrecorded riders.
public sealed class MarkTransportTripAwaitingNoShowRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, 500)] public int ExpectedAwaitingCount { get; set; }
}
public sealed class TransportTripNoShowPreviewDto
{
    public int RiderCount { get; set; }
    public int AwaitingCount { get; set; }
    public int BoardedCount { get; set; }
    public int DroppedCount { get; set; }
    public int NoShowCount { get; set; }
}
public sealed class TransportTripNoShowBatchResultDto
{
    public Guid TripReference { get; set; }
    public int RecordedCount { get; set; }
    public bool AlreadyRecorded { get; set; }
    public int RemainingAwaiting { get; set; }
}
