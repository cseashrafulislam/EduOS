using EduOS.Core.Entities.Finance;
using System.Text.Json.Serialization;

namespace EduOS.Core.DTOs.Finance;

public sealed class TransportBillingPolicyDto
{
    public long CampusId { get; set; }
    public TransportBillingCalculationMode CalculationMode { get; set; }
    public bool AllowManualOverride { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class RequestTransportClearanceDto
{
    public Guid ClientRequestId { get; set; }
    public Guid SourceAssignmentReference { get; set; }
    public TransportClearanceOperation Operation { get; set; }
    public Guid? DestinationVehicleReference { get; set; }
    public DateOnly EffectiveDate { get; set; }
    [Required] public string SourceRowVersion { get; set; } = string.Empty;
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = string.Empty;
    public decimal? ManualFareCredit { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualNetAdjustment { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualAdjustmentDelta { get; set; }
    [MaxLength(1000)] public string? OverrideReason { get; set; }
}

public sealed class ReviewTransportClearanceDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    public bool NoAdjustmentRequired { get; set; }
    public long? SettlementJournalId { get; set; }
    /// <summary>Finance may issue a new student invoice and post its GL for a positive period delta.</summary>
    public bool AutoPostInvoice { get; set; }
    public long? AccountingPeriodId { get; set; }
    public long? AutoFeeHeadId { get; set; }
    public decimal? ManualFareCredit { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualNetAdjustment { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualAdjustmentDelta { get; set; }
    [MaxLength(1000)] public string? OverrideReason { get; set; }
    [Required, StringLength(1000, MinimumLength = 20)] public string ReconciliationNote { get; set; } = string.Empty;
}

public sealed class TransportClearanceDto
{
    public long Id { get; set; }
    public Guid SourceAssignmentReference { get; set; }
    public long CampusId { get; set; }
    public TransportClearanceOperation Operation { get; set; }
    public TransportClearanceState State { get; set; }
    public Guid? DestinationVehicleReference { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public TransportBillingCalculationMode CalculationMode { get; set; }
    public decimal MonthlyFareAtRequest { get; set; }
    public decimal SuggestedFareCredit { get; set; }
    public decimal ApprovedFareCredit { get; set; }
    public decimal SuggestedNetAdjustment { get; set; }
    public decimal ApprovedNetAdjustment { get; set; }
    public string? OverrideReason { get; set; }
    public string RequestReason { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public bool NoAdjustmentRequired { get; set; }
    public long? SettlementJournalId { get; set; }
    public string? ReconciliationNote { get; set; }
    public long? ReviewedByUserId { get; set; }
    public long? SuccessorAssignmentId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
