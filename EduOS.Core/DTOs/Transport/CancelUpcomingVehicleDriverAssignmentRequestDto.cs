using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;
public sealed class CancelUpcomingVehicleDriverAssignmentRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
