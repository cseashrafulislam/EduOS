using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;

public sealed class ShortenUpcomingRouteVehicleRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public DateOnly EffectiveTo { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
