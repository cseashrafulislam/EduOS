namespace EduOS.Core.DTOs.Hostel;

/// <summary>Future-date room occupancy from canonical beds and stays; never includes student or financial data.</summary>
public sealed class HostelOccupancyForecastRowDto
{
    public long RoomId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int ActiveBeds { get; set; }
    public int ReservedBeds { get; set; }
    public int AvailableBeds { get; set; }
    public int InactiveReservedBeds { get; set; }
    public int DuplicateReservations { get; set; }
    public bool NeedsAttention { get; set; }
    public string Status { get; set; } = string.Empty;
}
