using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance;

/// <summary>One human-reviewed benefit mode per application. Fee credits are held in
/// canonical StudentDiscount; a cash grant is disbursed only by a reviewed Finance journal.</summary>
public sealed class ScholarshipApplication : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long? DiscountRuleId { get; set; }
    [Required, MaxLength(12)] public string AwardMode { get; set; } = "FeeCredit";
    [Column(TypeName = "decimal(18,2)")] public decimal? CashAmount { get; set; }
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

/// <summary>One cash disbursement instruction and immutable evidence of its Finance posting.
/// Physical cash or bank transfer occurs outside EduOS; this record never initiates funds.</summary>
public sealed class ScholarshipDisbursement : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public long ScholarshipApplicationId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, MaxLength(8)] public string Method { get; set; } = "Cash";
    [Required, MaxLength(8)] public string BeneficiaryType { get; set; } = "Student";
    public long? GuardianId { get; set; }
    [Required, MaxLength(200)] public string BeneficiaryNameSnapshot { get; set; } = "";
    public long ExpenseAccountId { get; set; }
    public long PaymentAccountId { get; set; }
    public long? BankAccountId { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly PaymentDate { get; set; }
    [Required, MaxLength(100)] public string ExternalPaymentReference { get; set; } = "";
    [Required, MaxLength(16)] public string State { get; set; } = "Submitted";
    public long PreparedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(500)] public string? ReviewReason { get; set; }
    public long? JournalId { get; set; }
    public long? ReversalJournalId { get; set; }
    public Guid? ReversalRequestId { get; set; }
    public Guid? ReversalReviewRequestId { get; set; }
    public long? ReversalRequestedByUserId { get; set; }
    public DateTime? ReversalRequestedAtUtc { get; set; }
    public long? ReversalAccountingPeriodId { get; set; }
    public DateOnly? ReversalDate { get; set; }
    [MaxLength(100)] public string? ExternalReversalReference { get; set; }
    [MaxLength(500)] public string? ReversalReason { get; set; }
    public long? ReversedByUserId { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
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
