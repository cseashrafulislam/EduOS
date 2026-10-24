namespace EduOS.Core.DTOs.Transport;

public sealed class ExtendOperatingVehicleDriverRequestDto
{
    [Required] public DateOnly EffectiveTo { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
