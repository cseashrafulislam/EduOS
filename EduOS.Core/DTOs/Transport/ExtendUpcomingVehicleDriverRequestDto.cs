using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;

/// <summary>Extend the end date of an existing future vehicle-driver reservation.</summary>
public sealed class ExtendUpcomingVehicleDriverRequestDto
{
    public DateOnly EffectiveTo { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
