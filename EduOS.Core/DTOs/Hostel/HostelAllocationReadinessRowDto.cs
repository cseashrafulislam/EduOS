namespace EduOS.Core.DTOs.Hostel;

/// <summary>Staff-only readiness queue; no student names, codes, contacts or monetary fields.</summary>
public sealed class HostelAllocationReadinessRowDto
{
    public long AllocationId { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public string Readiness { get; set; } = string.Empty;
    public bool NeedsAttention { get; set; }
}
