using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;

public sealed class ChangeUpcomingRouteVehicleRequestDto
{
    [Range(1, long.MaxValue)] public long NewVehicleId { get; set; }
    [Required] public string AssignmentRowVersion { get; set; } = string.Empty;
    [Required] public string VehicleRowVersion { get; set; } = string.Empty;
}

public sealed class UpcomingRouteVehicleChoiceDto
{
    public long VehicleId { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
