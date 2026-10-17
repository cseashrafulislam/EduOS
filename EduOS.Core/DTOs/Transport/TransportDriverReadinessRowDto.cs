namespace EduOS.Core.DTOs.Transport;

public sealed class TransportDriverReadinessRowDto
{
    public Guid VehicleReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Capacity { get; set; }
    public int ActiveRiders { get; set; }
    public int CurrentRouteLinks { get; set; }
    public int CurrentDriverLinks { get; set; }
    public string ReadinessState { get; set; } = string.Empty;
    public bool NeedsAttention { get; set; }
    public DateOnly? LicenseExpiresOn { get; set; }
    public int? DaysUntilExpiry { get; set; }
}
