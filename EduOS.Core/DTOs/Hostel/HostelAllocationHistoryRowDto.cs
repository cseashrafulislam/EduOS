namespace EduOS.Core.DTOs.Hostel;

/// <summary>Privacy-minimized hostel allocation history; intentionally excludes rent, deposits and contact details.</summary>
public sealed class HostelAllocationHistoryRowDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public HostelAllocationState State { get; set; }
}
