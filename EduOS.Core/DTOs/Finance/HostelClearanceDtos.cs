using EduOS.Core.Entities.Finance;
using System.Text.Json.Serialization;

namespace EduOS.Core.DTOs.Finance;

public sealed class RequestHostelClearanceDto
{
    public Guid ClientRequestId { get; set; }
    public long SourceAllocationId { get; set; }
    public HostelClearanceOperation Operation { get; set; }
    public long? DestinationBedId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    [Required] public string SourceRowVersion { get; set; } = string.Empty;
    public string? DestinationBedRowVersion { get; set; }
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = string.Empty;
    public decimal? ManualRentCredit { get; set; }
    public decimal? ManualDepositRefund { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualNetAdjustment { get; set; }
    /// <summary>Optional signed amount added to the calculated amount (mutually exclusive with manual final).</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualAdjustmentDelta { get; set; }
    [MaxLength(1000)] public string? OverrideReason { get; set; }
}

public sealed class HostelBillingPolicyDto
{
    public long CampusId { get; set; }
    public HostelBillingCalculationMode CalculationMode { get; set; }
    public bool AllowManualOverride { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class ReviewHostelClearanceDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    public bool NoAdjustmentRequired { get; set; }
    public long? SettlementJournalId { get; set; }
    /// <summary>Finance may issue a new student invoice and post its GL for a positive period delta.</summary>
    public bool AutoPostInvoice { get; set; }
    public long? AccountingPeriodId { get; set; }
    public long? AutoFeeHeadId { get; set; }
    public decimal? ManualRentCredit { get; set; }
    public decimal? ManualDepositRefund { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualNetAdjustment { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualAdjustmentDelta { get; set; }
    [MaxLength(1000)] public string? OverrideReason { get; set; }
    [Required, StringLength(1000, MinimumLength = 20)] public string ReconciliationNote { get; set; } = string.Empty;
}

public sealed class HostelClearanceDto
{
    public long Id { get; set; }
    public long SourceAllocationId { get; set; }
    public long CampusId { get; set; }
    public HostelClearanceOperation Operation { get; set; }
    public HostelClearanceState State { get; set; }
    public long? DestinationBedId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string RequestReason { get; set; } = string.Empty;
    public decimal MonthlyRentAtRequest { get; set; }
    public decimal DepositAtRequest { get; set; }
    public long RequestedByUserId { get; set; }
    public HostelBillingCalculationMode CalculationMode { get; set; }
    public decimal SuggestedRentCredit { get; set; }
    public decimal SuggestedDepositRefund { get; set; }
    public decimal ApprovedRentCredit { get; set; }
    public decimal ApprovedDepositRefund { get; set; }
    public decimal SuggestedNetAdjustment { get; set; }
    public decimal ApprovedNetAdjustment { get; set; }
    public string? OverrideReason { get; set; }
    public bool NoAdjustmentRequired { get; set; }
    public long? SettlementJournalId { get; set; }
    public string? ReconciliationNote { get; set; }
    public long? ReviewedByUserId { get; set; }
    public long? SuccessorAllocationId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
