using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance;

public enum TransportClearanceOperation { VehicleTransfer = 1, Closure = 2, UpcomingRiderExtension = 3, UpcomingRiderShortening = 4 }
public enum TransportClearanceState { Pending = 1, Approved = 2, Rejected = 3, Consumed = 4 }
public enum TransportBillingCalculationMode { DailyProrated = 1, FixedMonthly = 2, Manual = 3 }

public sealed class TransportBillingPolicy : BaseTenantEntity
{
    public long CampusId { get; set; }
    public TransportBillingCalculationMode CalculationMode { get; set; } = TransportBillingCalculationMode.DailyProrated;
    public bool AllowManualOverride { get; set; } = true;
}

/// <summary>Finance approval for one exact operating rider transfer or closure; never a cash payout.</summary>
public sealed class TransportFinanceClearance : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(64)] public string RequestHash { get; set; } = string.Empty;
    public long SourceAssignmentId { get; set; }
    public long CampusId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public TransportClearanceOperation Operation { get; set; }
    public long? DestinationVehicleId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    [Required, MaxLength(128)] public string SourceVersion { get; set; } = string.Empty;
    [MaxLength(128)] public string? DestinationVersion { get; set; }
    [Required, MaxLength(64)] public string FinanceSnapshotHash { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string RequestReason { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public TransportBillingCalculationMode CalculationMode { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MonthlyFareAtRequest { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SuggestedFareCredit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ApprovedFareCredit { get; set; }
    /// <summary>Signed period difference; positive is additional fare, negative is possible credit.</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal SuggestedNetAdjustment { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ApprovedNetAdjustment { get; set; }
    [MaxLength(1000)] public string? OverrideReason { get; set; }
    public TransportClearanceState State { get; set; } = TransportClearanceState.Pending;
    public bool NoAdjustmentRequired { get; set; }
    public long? SettlementJournalId { get; set; }
    [MaxLength(1000)] public string? ReconciliationNote { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ConsumedByUserId { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public long? SuccessorAssignmentId { get; set; }
}
