using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;

/// <summary>Move the first effective day of an existing future route/vehicle link.</summary>
public sealed class RescheduleUpcomingRouteVehicleRequestDto
{
    public DateOnly EffectiveFrom { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
