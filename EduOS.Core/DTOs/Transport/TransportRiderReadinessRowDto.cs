namespace EduOS.Core.DTOs.Transport;

public sealed class TransportRiderReadinessRowDto
{
    public Guid AssignmentReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Readiness { get; set; } = string.Empty;
    public bool NeedsAttention { get; set; }
}
