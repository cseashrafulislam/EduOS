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
    public bool RedactionRequested { get; set; }
    public bool PrivacyRedacted { get; set; }
    // Read-only projections derived from the existing source-linked event history.
    public string IncidentStateCode { get; set; } = "None";
    public string LegalHoldStateCode { get; set; } = "None";
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

// An export is a privileged audited write, not an unaudited GET download.
public sealed class FacilityVisitorExportRequestDto
{
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
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

/// <summary>Authorized campus admission count; never includes visitor identity or badge.</summary>
public sealed class FacilityVisitorOccupancyDto
{
    public long CampusId { get; set; }
    public int CheckedInCount { get; set; }
    // PII-free count of checked-in appointments beyond their approved end.
    public int OverdueCount { get; set; }
    public int? MaxConcurrent { get; set; }
    public DateTime AsOfUtc { get; set; }
}

public sealed class FacilityVisitorPolicyRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Required, MaxLength(24)] public string PolicyCode { get; set; } = string.Empty;
    [Required, MaxLength(12)] public string ProposedValue { get; set; } = string.Empty;
    [Required, MinLength(10), MaxLength(200)] public string Reason { get; set; } = string.Empty;
}

public sealed class FacilityVisitorPolicyDecisionDto
{
    public Guid ClientCommandId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MinLength(10), MaxLength(200)] public string Reason { get; set; } = string.Empty;
}

public sealed class FacilityVisitorPolicyDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string PolicyCode { get; set; } = string.Empty;
    public string ProposedValue { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public string RequestReason { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
