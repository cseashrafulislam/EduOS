using System.ComponentModel.DataAnnotations;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Facilities;

/// <summary>Minimal internal visitor appointment; no national ID, phone, image or identity document.</summary>
public sealed class FacilityVisitorVisit : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long HostUserId { get; set; }
    [Required, MaxLength(100)] public string VisitorName { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Purpose { get; set; } = string.Empty;
    public DateTime VisitStartUtc { get; set; }
    public DateTime VisitEndUtc { get; set; }
    [Required, MaxLength(24)] public string StateCode { get; set; } = "PendingHost";
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    [MaxLength(20)] public string? BadgeCode { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }
    public DateTime? CheckedOutAtUtc { get; set; }
}

/// <summary>Source-linked action/actor/time audit. Badge and reason are private payloads, cleared only by independently approved retention redaction; immutable action metadata is retained.</summary>
public sealed class FacilityVisitorEvent : BaseTenantEntity
{
    public long FacilityVisitorVisitId { get; set; }
    public Guid ClientCommandId { get; set; }
    public long ActorUserId { get; set; }
    [Required, MaxLength(24)] public string ActionCode { get; set; } = string.Empty;
    [Required, MaxLength(24)] public string FromState { get; set; } = string.Empty;
    [Required, MaxLength(24)] public string ToState { get; set; } = string.Empty;
    [MaxLength(20)] public string? BadgeCode { get; set; }
    [MaxLength(200)] public string? Reason { get; set; }
}
