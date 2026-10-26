using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;

public sealed class ShortenUpcomingVehicleDriverRequestDto
{
    [Required] public DateOnly EffectiveTo { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
