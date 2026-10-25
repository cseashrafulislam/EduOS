namespace EduOS.Core.DTOs.Transport;

public sealed class ExtendUpcomingRouteVehicleRequestDto
{
    [Required] public DateOnly EffectiveTo { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
