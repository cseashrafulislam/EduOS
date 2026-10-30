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
public sealed class ScheduleTransportTripsRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid RouteReference { get; set; }
    public Guid VehicleReference { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    [Required, MaxLength(10)] public string Direction { get; set; } = string.Empty;
    public bool SkipFridaySaturday { get; set; }
    public bool UseAcademicCalendar { get; set; }
}
public sealed class TransportTripScheduleResultDto
{
    public int SelectedDays { get; set; }
    public int NewlyPlanned { get; set; }
    public int AlreadyPlanned { get; set; }
    public IReadOnlyList<DateOnly> SkippedDates { get; set; } = Array.Empty<DateOnly>();
    public DateOnly? FailedDate { get; set; }
    public int? FailureStatusCode { get; set; }
    public string? FailureMessage { get; set; }
    public IReadOnlyList<TransportTripRowDto> Trips { get; set; } = Array.Empty<TransportTripRowDto>();
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

public sealed class TransportTripCalendarDayDto
{
    public DateOnly Date { get; set; }
    public bool IsOperatingDay { get; set; }
    public string? ClosureReason { get; set; }
}
public sealed class TransportTripCalendarDto
{
    public long CampusId { get; set; }
    public IReadOnlyList<TransportTripCalendarDayDto> Days { get; set; } =
        Array.Empty<TransportTripCalendarDayDto>();
}
