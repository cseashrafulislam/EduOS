namespace EduOS.Core.DTOs.Transport;

/// <summary>Privacy-minimized fleet aggregates derived from the existing campus-authorized capacity projection.</summary>
public sealed class TransportFleetInsightsDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public int TotalVehicles { get; set; }
    public int ActiveVehicles { get; set; }
    public long TotalSeats { get; set; }
    public long ActiveRiders { get; set; }
    public long AvailableSeats { get; set; }
    public int OverCapacityVehicles { get; set; }
    public int WithoutCurrentRouteVehicles { get; set; }
    public int InactiveWithRidersVehicles { get; set; }
    public int HighUtilizationVehicles { get; set; }
    public int RiskVehicles { get; set; }
    public decimal FleetUtilizationPercent { get; set; }
}
