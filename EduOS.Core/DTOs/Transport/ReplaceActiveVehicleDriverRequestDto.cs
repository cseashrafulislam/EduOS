namespace EduOS.Core.DTOs.Transport;

public sealed class ReplaceActiveVehicleDriverRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long NewDriverId { get; set; }
    [Required] public string AssignmentRowVersion { get; set; } = string.Empty;
    [Required] public string DriverRowVersion { get; set; } = string.Empty;
}
