using EduOS.Core.Entities.Base;
namespace EduOS.Core.Entities.Transport;
public sealed class TransportTrip : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public Guid? DispatchRequestId { get; set; }
    public long CampusId { get; set; }
    public long RouteId { get; set; }
    public long VehicleId { get; set; }
    public long? TransportDriverId { get; set; }
    public DateOnly OperatingDate { get; set; }
    [Required, MaxLength(10)] public string Direction { get; set; } = string.Empty;
    [Required, MaxLength(15)] public string State { get; set; } = "Planned";
    public int RiderCount { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public long? DispatchedBy { get; set; }
}
public sealed class TransportTripRider : BaseTenantEntity
{
    public long TransportTripId { get; set; }
    public long StudentTransportId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    [Required, MaxLength(60)] public string StudentCodeSnapshot { get; set; } = string.Empty;
    public long PickupStopId { get; set; }
    public long DropStopId { get; set; }
    [Required, MaxLength(150)] public string PickupStopSnapshot { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string DropStopSnapshot { get; set; } = string.Empty;
}
