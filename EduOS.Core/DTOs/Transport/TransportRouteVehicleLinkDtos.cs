using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;
public sealed class CreateTransportRouteVehicleLinkRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long RouteId { get; set; }
    [Range(1, long.MaxValue)] public long VehicleId { get; set; }
    [Required] public string RouteRowVersion { get; set; } = string.Empty;
    [Required] public string VehicleRowVersion { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
public sealed class TransportRouteVehicleLinkRowDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public long VehicleId { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int ActiveRiders { get; set; }
    public int OccupiedToday { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
