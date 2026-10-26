namespace EduOS.Core.DTOs.Transport;

public sealed class ChangeUpcomingVehicleDriverRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long NewDriverId { get; set; }
    [Required] public string AssignmentRowVersion { get; set; } = string.Empty;
    [Required] public string DriverRowVersion { get; set; } = string.Empty;
}

public sealed class UpcomingVehicleDriverChoiceDto
{
    public long DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public DateOnly LicenseExpiresOn { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
