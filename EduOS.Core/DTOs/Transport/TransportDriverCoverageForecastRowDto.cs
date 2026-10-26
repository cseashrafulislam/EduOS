namespace EduOS.Core.DTOs.Transport;

/// <summary>Privacy-minimized dispatch readiness on one future operating date.</summary>
public sealed class TransportDriverCoverageForecastRowDto
{
    public Guid VehicleReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public DateOnly OperatingDate { get; set; }
    public int Capacity { get; set; }
    public int ReservedRiders { get; set; }
    public int RouteLinks { get; set; }
    public int DriverLinks { get; set; }
    public DateOnly? LicenseExpiresOn { get; set; }
    public string Readiness { get; set; } = string.Empty;
    public bool NeedsAttention { get; set; }
}
