namespace EduOS.Core.DTOs.Hostel;

/// <summary>Privacy-minimized checkout queue; no student identity or financial fields.</summary>
public sealed class HostelCheckoutFollowUpRowDto
{
    public long AllocationId { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int DaysUntilCheckout { get; set; }
    public string FollowUp { get; set; } = string.Empty;
}
