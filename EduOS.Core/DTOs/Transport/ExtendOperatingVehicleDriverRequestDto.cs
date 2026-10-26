namespace EduOS.Core.DTOs.Transport;

public sealed class ExtendOperatingVehicleDriverRequestDto
{
    // Optional for older clients; current UI sends a stable key for retries.
    public Guid ClientRequestId { get; set; }
    [Required] public DateOnly EffectiveTo { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
