using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Assessment;

public enum AssessmentMarkReviewDecision { Submitted = 1, Rejected = 2, Approved = 3 }

/// <summary>Append-only, tenant-owned decision; source rowversion binds approval to exact marks evidence.</summary>
public sealed class AssessmentMarkReviewEvent : BaseTenantEntity
{
    public long AssessmentSubjectId { get; set; }
    public Guid ClientRequestId { get; set; }
    public AssessmentMarkReviewDecision Decision { get; set; }
    public long ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; }
    [Required, MaxLength(128)] public string MarkRowVersion { get; set; } = "";
    [MaxLength(500)] public string? Reason { get; set; }
}
