namespace EduOS.Core.DTOs.Hostel;

/// <summary>Pickup-authorized guardian view of today's scheduled hostel stays. No fee/contact data.</summary>
public sealed class GuardianHostelStayRowDto
{
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
