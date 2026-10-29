namespace EduOS.Core.DTOs.Hostel;

/// <summary>
/// Campus-authorized scheduled occupancy for one date. This is not proof of physical
/// check-in or attendance. No contacts, guardian details, rent or deposits.
/// </summary>
public sealed class HostelResidentRosterRowDto
{
    public long AllocationId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string CurrentState { get; set; } = string.Empty;
}
