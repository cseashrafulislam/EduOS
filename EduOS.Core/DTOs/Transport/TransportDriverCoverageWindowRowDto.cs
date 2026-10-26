namespace EduOS.Core.DTOs.Transport;

/// <summary>Privacy-minimized dispatch risk over an inclusive planning window.</summary>
public sealed class TransportDriverCoverageWindowRowDto
{
    public Guid VehicleReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int Capacity { get; set; }
    public int PeakReservedRiders { get; set; }
    public DateOnly PeakDate { get; set; }
    public int MinimumFreeSeats { get; set; }
    public int AttentionDays { get; set; }
    public DateOnly? FirstAttentionDate { get; set; }
    public string FirstIssue { get; set; } = string.Empty;
    public bool NeedsAttention => AttentionDays > 0;
}
