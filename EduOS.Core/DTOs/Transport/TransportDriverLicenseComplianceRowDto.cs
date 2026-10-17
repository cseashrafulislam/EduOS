namespace EduOS.Core.DTOs.Transport;

public sealed class TransportDriverLicenseComplianceRowDto
{
    public long DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public DateOnly? LicenseExpiresOn { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public string ComplianceState { get; set; } = string.Empty;
    public bool HasCurrentVehicle { get; set; }
}
