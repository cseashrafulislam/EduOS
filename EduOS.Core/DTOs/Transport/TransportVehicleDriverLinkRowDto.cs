namespace EduOS.Core.DTOs.Transport;

public sealed class TransportVehicleDriverLinkRowDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long VehicleId { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public long TransportDriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public DateOnly? LicenseExpiresOn { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsScheduled { get; set; }
    public bool IsCancelled { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
