using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;
public sealed class CancelUpcomingRouteVehicleLinkRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
