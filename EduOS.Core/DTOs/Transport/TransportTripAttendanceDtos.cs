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
    public string StudentCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public DateOnly OperatingDate { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string AttendanceState { get; set; } = string.Empty;
    public DateTime? LastEventAt { get; set; }
}
