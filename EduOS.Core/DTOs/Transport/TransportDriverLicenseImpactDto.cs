namespace EduOS.Core.DTOs.Transport;

/// <summary>Tenant-admin-only, privacy-minimized preflight for a proposed driver status/license change.
/// The save command must independently revalidate all assignments inside its transaction.</summary>
public sealed class TransportDriverLicenseImpactDto
{
    public long DriverId { get; set; }
    public DateOnly? ProposedExpiry { get; set; }
    public bool ProposedActive { get; set; }
    public int ActiveOrScheduledAssignments { get; set; }
    public int ScheduledAssignments { get; set; }
    public int OpenEndedAssignments { get; set; }
    public int BlockingAssignments { get; set; }
    public DateOnly? LatestFiniteAssignmentEnd { get; set; }
    public bool CanApply { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
