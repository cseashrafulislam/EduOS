namespace EduOS.Core.DTOs.Transport;
public sealed class TransportCapacityReportRowDto
{
    public Guid VehicleReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string? VehicleType { get; set; }
    public bool IsActive { get; set; }
    public int Capacity { get; set; }
    public int ActiveRiders { get; set; }
    public int AvailableSeats { get; set; }
    public int CurrentRouteLinks { get; set; }
    public decimal UtilizationPercent { get; set; }
    public bool OverCapacity { get; set; }
    public bool HasRisk { get; set; }
}
