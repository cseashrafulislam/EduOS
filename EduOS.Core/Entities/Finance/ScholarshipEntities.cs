using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance;

/// <summary>Application and decision only. Award amounts live in the canonical DiscountRule;
/// actual future fee reductions live in StudentDiscount and its invoice lineage.</summary>
public sealed class ScholarshipApplication : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long DiscountRuleId { get; set; }
    public long? EvidenceFileAssetId { get; set; }
    public long? RenewalOfApplicationId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    [Required, MaxLength(700)] public string EligibilityStatement { get; set; } = "";
    [Required, MaxLength(20)] public string State { get; set; } = "Submitted";
    public long SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
    public long? StudentDiscountId { get; set; }
    public DateTime? AwardedAtUtc { get; set; }
}

/// <summary>Append-only, purpose-limited decision/reversal evidence; no private attachment text.</summary>
public sealed class ScholarshipApplicationEvent : BaseTenantEntity
{
    public long ScholarshipApplicationId { get; set; }
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(24)] public string Action { get; set; } = "";
    [MaxLength(500)] public string Reason { get; set; } = "";
    public long ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
