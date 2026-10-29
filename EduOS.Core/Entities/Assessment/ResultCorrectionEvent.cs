using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Assessment;

/// <summary>
/// One immutable, independently authorized re-scrutiny amendment. The before/after
/// subject AND component evidence is retained separately from operational audit
/// retention. Historical publication snapshots are never rewritten.
/// </summary>
public sealed class ResultCorrectionEvent : BaseTenantEntity
{
    public Guid CaseReference { get; set; }
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(64)] public string RequestDigest { get; set; } = string.Empty;
    public long PreviousPublicationId { get; set; }
    public long NewPublicationId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public int PreviousVersionNo { get; set; }
    public int NewVersionNo { get; set; }
    public int CorrectedSubjectCount { get; set; }
    public long ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string BeforeJson { get; set; } = string.Empty;
    [Required] public string AfterJson { get; set; } = string.Empty;
}
