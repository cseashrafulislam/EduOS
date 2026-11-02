using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Facilities;

public sealed class CreateFacilityVisitorDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Range(1, long.MaxValue)] public long HostUserId { get; set; }
    [Required, MaxLength(100)] public string VisitorName { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Purpose { get; set; } = string.Empty;
    public DateTime VisitStartUtc { get; set; }
    public DateTime VisitEndUtc { get; set; }
}
public sealed class FacilityVisitorTransitionDto
{
    public Guid ClientCommandId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MaxLength(20)] public string? BadgeCode { get; set; }
    [MaxLength(200)] public string? Reason { get; set; }
}
public sealed class FacilityVisitorDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public long HostUserId { get; set; }
    public string VisitorName { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTime VisitStartUtc { get; set; }
    public DateTime VisitEndUtc { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public string? BadgeCode { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }
    public DateTime? CheckedOutAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class FacilityVisitorHostDto
{
    public long UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
public sealed class FacilityVisitorEventDto
{
    public string ActionCode { get; set; } = string.Empty;
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public long ActorUserId { get; set; }
    // Incident narrative is visible only to authorized Facilities desk staff.
    public string? Reason { get; set; }
}

// PII-minimized operational export: no visitor name, purpose, badge, host or incident narrative.
public sealed class FacilityVisitorExportRowDto
{
    public long VisitId { get; set; }
    public long CampusId { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public DateTime VisitStartUtc { get; set; }
    public DateTime VisitEndUtc { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }
    public DateTime? CheckedOutAtUtc { get; set; }
    public int IncidentCount { get; set; }
}
