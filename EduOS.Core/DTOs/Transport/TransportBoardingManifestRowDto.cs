namespace EduOS.Core.DTOs.Transport;

/// <summary>Authorized, privacy-minimized planning roster. This is not a recorded trip or attendance event.</summary>
public sealed class TransportBoardingManifestRowDto
{
    public Guid AssignmentReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string PickupStop { get; set; } = string.Empty;
    public string DropStop { get; set; } = string.Empty;
    public DateOnly OperatingDate { get; set; }
    public string Readiness { get; set; } = string.Empty;
    public bool NeedsAttention { get; set; }
}
