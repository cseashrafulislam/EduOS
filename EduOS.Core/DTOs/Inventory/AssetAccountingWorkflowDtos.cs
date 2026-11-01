namespace EduOS.Core.DTOs.Inventory;

public sealed class AssetAccountingOptionsDto
{
    public List<ProcurementInvoicePeriodOptionDto> Periods { get; set; } = [];
    public List<ProcurementInvoiceAccountOptionDto> Accounts { get; set; } = [];
}

/// <summary>One explicitly selected physical unit from an approved, still-posted receipt.
/// Auto, manual final, or automatic with documented user correction remain tenant choices.</summary>
public sealed class CreateAssetCapitalizationRequest
{
    public Guid ClientRequestId { get; set; }
    public string AssetRowVersion { get; set; } = "";
    public Guid ReceiptReference { get; set; }
    public int ReceiptLineNo { get; set; }
    public int ReceiptUnitOrdinal { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly CapitalizationDate { get; set; }
    public DateOnly InServiceDate { get; set; }
    public string PolicyMode { get; set; } = "Automatic";
    public decimal ProposedCostBasis { get; set; }
    public decimal SalvageValue { get; set; }
    public string DepreciationMethod { get; set; } = "StraightLine";
    public int UsefulLifeMonths { get; set; }
    public int PolicyVersion { get; set; } = 1;
    public long FixedAssetAccountId { get; set; }
    public long InventoryAccountId { get; set; }
    public long AccumulatedDepreciationAccountId { get; set; }
    public long DepreciationExpenseAccountId { get; set; }
    public long ImpairmentExpenseAccountId { get; set; }
    public long DisposalGainAccountId { get; set; }
    public long DisposalLossAccountId { get; set; }
    public long? VarianceGainAccountId { get; set; }
    public long? VarianceLossAccountId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AssetCapitalizationRecordDto
{
    public Guid Reference { get; set; }
    public Guid AssetReference { get; set; }
    public long CampusId { get; set; }
    public string State { get; set; } = "";
    public decimal ProposedCostBasis { get; set; }
    public decimal? PostedStockCost { get; set; }
    public string PolicyMode { get; set; } = "";
    public long ReceiptLineId { get; set; }
    public int ReceiptUnitOrdinal { get; set; }
    public long? AssetBookId { get; set; }
    public Guid? StockMovementReference { get; set; }
    public Guid? FinanceJournalReference { get; set; }
    public Guid? StockReversalReference { get; set; }
    public Guid? FinanceReversalReference { get; set; }
    public Guid? PendingReversalReference { get; set; }
    public string? PendingReversalRowVersion { get; set; }
    public long? MakerId { get; set; }
    public long? ReviewerId { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class ReviewAssetCapitalizationRequest
{
    public string RowVersion { get; set; } = "";
    public bool Approve { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AssetValuationRequest
{
    public Guid ClientRequestId { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string ValuationMode { get; set; } = "Automatic";
    public decimal Amount { get; set; }
    public long? ProceedsClearingAccountId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AssetReviewRequest
{
    public string RowVersion { get; set; } = "";
    public bool Approve { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AssetReversalRequest
{
    public Guid ClientRequestId { get; set; }
    public string OriginalRowVersion { get; set; } = "";
    public long AccountingPeriodId { get; set; }
    public DateOnly ReversalDate { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class CreateAssetDepreciationPolicyRequest
{
    public Guid ClientRequestId { get; set; }
    public string AssetBookRowVersion { get; set; } = "";
    public long AccountingPeriodId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string Mode { get; set; } = "Automatic";
    public int RemainingUsefulLifeMonths { get; set; }
    public int RemainingLifeCorrectionMonths { get; set; }
    public decimal NewSalvageValue { get; set; }
    public int NewPolicyVersion { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AssetDepreciationPolicyDto
{
    public Guid Reference { get; set; }
    public Guid AssetReference { get; set; }
    public string State { get; set; } = "";
    public string Mode { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public int PreviousPolicyVersion { get; set; }
    public int NewPolicyVersion { get; set; }
    public int SuggestedRemainingMonths { get; set; }
    public int ApprovedRemainingMonths { get; set; }
    public decimal OpeningCarryingValue { get; set; }
    public decimal NewSalvageValue { get; set; }
    public long? MakerId { get; set; }
    public long? ReviewerId { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class AssetAccountingEventDto
{
    public string Kind { get; set; } = "";
    public long Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public string State { get; set; } = "";
    public DateOnly EffectiveDate { get; set; }
    public decimal Amount { get; set; }
    public Guid? JournalReference { get; set; }
    public Guid? ReversalJournalReference { get; set; }
    public Guid? PendingReversalReference { get; set; }
    public string? PendingReversalRowVersion { get; set; }
    public long? MakerId { get; set; }
    public long? ReviewerId { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class AssetAccountingStatementDto
{
    public Guid AssetReference { get; set; }
    public long? AssetBookId { get; set; }
    public decimal CostBasis { get; set; }
    public decimal SalvageValue { get; set; }
    public decimal PostedDepreciation { get; set; }
    public decimal PostedImpairment { get; set; }
    public decimal CarryingValue { get; set; }
    public bool Disposed { get; set; }
    public bool CapitalizationReversed { get; set; }
    public int PolicyVersion { get; set; }
    public string PolicyMode { get; set; } = "Original";
    public int RemainingUsefulLifeMonths { get; set; }
    public string AssetBookRowVersion { get; set; } = "";
    public List<AssetDepreciationPolicyDto> PolicyHistory { get; set; } = [];
    public List<AssetAccountingEventDto> Events { get; set; } = [];
}
