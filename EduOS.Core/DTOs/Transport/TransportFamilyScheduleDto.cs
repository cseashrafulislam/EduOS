namespace EduOS.Core.DTOs.Transport;

/// <summary>Minimal current/future transport details visible to the learner or a linked guardian.
/// Never includes fare, internal IDs, driver contacts, tracking or attendance claims.</summary>
public sealed class TransportFamilyScheduleDto
{
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string? PickupStop { get; set; }
    public string? DropStop { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
