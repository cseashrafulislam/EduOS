namespace EduOS.Core.DTOs.Hostel;

/// <summary>Privacy-minimized planning availability; no student or monetary data.</summary>
public sealed class HostelBedAvailabilityRowDto
{
    public long BedId { get; set; }
    public long CampusId { get; set; }
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public int RoomCapacity { get; set; }
    public int RemainingRoomSlots { get; set; }
}
