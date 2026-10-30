using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;
public sealed class PlanTransportTripRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid RouteReference { get; set; }
    public Guid VehicleReference { get; set; }
    public DateOnly OperatingDate { get; set; }
    [Required, MaxLength(10)] public string Direction { get; set; } = string.Empty;
}
public sealed class DispatchTransportTripRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
public sealed class CompleteTransportTripRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
public sealed class TransportTripRowDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public DateOnly OperatingDate { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int RiderCount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class TransportTripRiderDto
{
    public string StudentCode { get; set; } = string.Empty;
    public string PickupStop { get; set; } = string.Empty;
    public string DropStop { get; set; } = string.Empty;
    public string AttendanceState { get; set; } = string.Empty;
}

public sealed class TransportTripManifestReportDto
{
    public TransportTripRowDto Trip { get; set; } = new();
    public IReadOnlyList<TransportTripRiderDto> Riders { get; set; } = Array.Empty<TransportTripRiderDto>();
    public int AwaitingCount { get; set; }
    public int BoardedCount { get; set; }
    public int DroppedCount { get; set; }
    public int NoShowCount { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}
