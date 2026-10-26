using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;

/// <summary>Change the start date of an existing future vehicle-driver assignment.</summary>
public sealed class RescheduleUpcomingVehicleDriverRequestDto
{
    public Guid ClientRequestId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
