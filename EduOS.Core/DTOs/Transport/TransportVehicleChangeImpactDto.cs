namespace EduOS.Core.DTOs.Transport;

/// <summary>Read-only, privacy-minimized impact of a proposed canonical Vehicle change.</summary>
public sealed class TransportVehicleChangeImpactDto
{
    public Guid Reference { get; set; }
    public int ProposedCapacity { get; set; }
    public bool ProposedActive { get; set; }
    public int ActiveReservationRecords { get; set; }
    public int OccupiedToday { get; set; }
    public int PeakReservedSeats { get; set; }
    public int CurrentRouteAssignments { get; set; }
    public int CurrentDriverAssignments { get; set; }
    public bool CanApply { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
