using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Assessment;

public enum ResultRescrutinyAction
{
    Requested = 1,
    Rejected = 2,
    AcceptedForCorrection = 3
}

/// <summary>
/// Immutable, tenant-owned request/decision event. Exactly one request and at most
/// one terminal decision per case are enforced by a tenant-composite unique key.
/// Acceptance authorizes investigation; it NEVER edits or republishes results.
/// </summary>
public sealed class ResultRescrutinyEvent : BaseTenantEntity
{
    public Guid CaseReference { get; set; }
    public Guid ClientRequestId { get; set; }
    public long ResultPublicationId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public int PublicationVersionNo { get; set; }
    public ResultRescrutinyAction Action { get; set; }
    public bool IsTerminal { get; set; }
    public long ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}
