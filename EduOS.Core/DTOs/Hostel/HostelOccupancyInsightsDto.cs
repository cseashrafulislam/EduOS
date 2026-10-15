namespace EduOS.Core.DTOs.Hostel;

/// <summary>Aggregate-only, campus-authorized hostel occupancy metrics. No student or finance data.</summary>
public sealed class HostelOccupancyInsightsDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public int TotalRooms { get; set; }
    public int ActiveRooms { get; set; }
    public long TotalCapacity { get; set; }
    public long ActiveBeds { get; set; }
    public long OccupiedBeds { get; set; }
    public long AvailableBeds { get; set; }
    public int FullRooms { get; set; }
    public int OverCapacityRooms { get; set; }
    public int InactiveRoomsWithAllocations { get; set; }
    public int RoomsWithBedCapacityMismatch { get; set; }
    public int UnprovisionedRooms { get; set; }
    public int HighUtilizationRooms { get; set; }
    public int RiskRooms { get; set; }
    public decimal OccupancyPercent { get; set; }
}
