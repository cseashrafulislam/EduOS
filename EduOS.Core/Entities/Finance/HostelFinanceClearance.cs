using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance;

public enum HostelClearanceOperation { Transfer = 1, Checkout = 2 }
public enum HostelClearanceState { Pending = 1, Approved = 2, Rejected = 3, Consumed = 4 }
public enum HostelBillingCalculationMode { DailyProrated = 1, FixedMonthly = 2, Manual = 3 }

/// <summary>Tenant/campus Finance policy; historical clearances snapshot their calculations.</summary>
public sealed class HostelBillingPolicy : BaseTenantEntity
{
    public long CampusId { get; set; }
    public HostelBillingCalculationMode CalculationMode { get; set; } = HostelBillingCalculationMode.DailyProrated;
    public bool AllowManualOverride { get; set; } = true;
}

/// <summary>
/// Finance-owned manual settlement attestation; approval is bound to the exact
/// operation and immutable reservation/bed/Finance snapshot. No auto ledger posting.
/// </summary>
public sealed class HostelFinanceClearance : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(64)] public string RequestHash { get; set; } = string.Empty;
    public long SourceAllocationId { get; set; }
    public long CampusId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public HostelClearanceOperation Operation { get; set; }
    public long? DestinationBedId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    [Required, MaxLength(128)] public string SourceVersion { get; set; } = string.Empty;
    [MaxLength(128)] public string? DestinationVersion { get; set; }
    [Required, MaxLength(64)] public string FinanceSnapshotHash { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string RequestReason { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public HostelBillingCalculationMode CalculationMode { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MonthlyRentAtRequest { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DepositAtRequest { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SuggestedRentCredit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SuggestedDepositRefund { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ApprovedRentCredit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ApprovedDepositRefund { get; set; }
    [MaxLength(1000)] public string? OverrideReason { get; set; }
    public HostelClearanceState State { get; set; } = HostelClearanceState.Pending;
    /// <summary>Finance's explicit assertion that no GL change is needed for this operation.</summary>
    public bool NoAdjustmentRequired { get; set; }
    public long? SettlementJournalId { get; set; }
    [MaxLength(1000)] public string? ReconciliationNote { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ConsumedByUserId { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public long? SuccessorAllocationId { get; set; }
}
