namespace EduOS.Core.DTOs.Hostel;

/// <summary>Room-level peak demand over a bounded future interval; no student or billing details.</summary>
public sealed class HostelPeakOccupancyForecastRowDto
{
    public long RoomId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int ActiveBeds { get; set; }
    public int PeakReservedBeds { get; set; }
    public DateOnly PeakDate { get; set; }
    public int MinimumAvailableBeds { get; set; }
    public int MaximumDuplicateBookings { get; set; }
    public int MaximumInactiveReservedBeds { get; set; }
    public int AttentionDays { get; set; }
    public bool NeedsAttention { get; set; }
    public string Status { get; set; } = string.Empty;
}
