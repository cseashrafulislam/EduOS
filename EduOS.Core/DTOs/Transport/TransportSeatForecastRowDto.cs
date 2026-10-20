namespace EduOS.Core.DTOs.Transport;

public sealed class TransportSeatForecastRowDto
{
    public Guid VehicleReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Capacity { get; set; }
    public int PeakReservedSeats { get; set; }
    public DateOnly PeakDate { get; set; }
    public int MinimumFreeSeats { get; set; }
    public bool OverCapacity { get; set; }
    public bool HasRisk { get; set; }
}
