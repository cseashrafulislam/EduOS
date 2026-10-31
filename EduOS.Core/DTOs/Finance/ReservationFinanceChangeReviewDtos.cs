using System.Text.Json.Serialization;

namespace EduOS.Core.DTOs.Finance;

/// <summary>Read-only review of a proposed date/term amendment. Never a financial approval.</summary>
public sealed class ReservationFinanceChangeReviewRequestDto
{
    public string Kind { get; set; } = "hostel";
    public long ReservationId { get; set; }
    public string Change { get; set; } = string.Empty;
    public DateOnly? ProposedStartDate { get; set; }
    public DateOnly? ProposedEndDate { get; set; }
    /// <summary>Optional signed Finance quote; positive is a charge, negative is a possible credit.</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualNetAdjustment { get; set; }
    /// <summary>Signed correction added to the automatic proposal (exclusive with ManualNetAdjustment).</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ManualAdjustmentDelta { get; set; }
    /// <summary>Required for a manual override; the preview itself never approves or posts money.</summary>
    public string? ManualOverrideReason { get; set; }
}

public sealed class ReservationFinanceChangeReviewDto
{
    public string Kind { get; set; } = string.Empty;
    public long ReservationId { get; set; }
    public long CampusId { get; set; }
    public string Change { get; set; } = string.Empty;
    public DateOnly CurrentStartDate { get; set; }
    public DateOnly? CurrentEndDate { get; set; }
    public DateOnly ProposedStartDate { get; set; }
    public DateOnly? ProposedEndDate { get; set; }
    public decimal MonthlyCharge { get; set; }
    public decimal SecurityDeposit { get; set; }
    /// <summary>Campus-selected mode, not a command to bill or post.</summary>
    public string CalculationMode { get; set; } = string.Empty;
    public decimal? OriginalPeriodEstimate { get; set; }
    public decimal? ProposedPeriodEstimate { get; set; }
    /// <summary>Positive means extra charge, negative means possible credit, not a payable/refund.</summary>
    public decimal? SuggestedNetAdjustment { get; set; }
    /// <summary>Chosen estimate for Finance review only; no monetary mutation is authorized.</summary>
    public decimal? SelectedNetAdjustment { get; set; }
    public bool ManualOverrideAllowed { get; set; }
    public bool ManualOverrideApplied { get; set; }
    public string AdjustmentSelectionMode { get; set; } = string.Empty;
    public bool QuoteNeedsManualReview { get; set; }
    public string QuoteBasis { get; set; } = string.Empty;
    public bool HasPostedInvoiceActivity { get; set; }
    public bool HasPostedPaymentActivity { get; set; }
    public bool HasUnpostedIssuedInvoice { get; set; }
    public bool RequiresApprovedFinanceAdjustment { get; set; }
    public bool RequiresBillingPolicy { get; set; }
    public bool FinanceExposureBlocksChange { get; set; }
    public bool FinancialMutationApproved { get; set; } = false;
    public string Status { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Notice { get; set; } =
        "Estimate only: no reservation-to-invoice provenance, posting or approval. " +
        "Operations must independently revalidate inside its write transaction.";
}
