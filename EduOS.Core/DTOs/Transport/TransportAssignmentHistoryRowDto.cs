using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Transport;

/// <summary>Privacy-minimized, campus-scoped history of canonical student transport assignments.</summary>
public sealed class TransportAssignmentHistoryRowDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public TransportAssignmentState State { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
