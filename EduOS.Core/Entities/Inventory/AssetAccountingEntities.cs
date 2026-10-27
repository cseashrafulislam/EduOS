using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

// Platform persistence contracts. Finance owns approval, journals and monetary posting;
// Inventory owns operational workflow/UI. No accounting entry is created by these entities.

public class AssetBook : BaseTenantEntity
{
    public long AssetId { get; set; }
    public long CampusId { get; set; }
    public DateOnly InServiceDate { get; set; }
    [Required, MaxLength(30)] public string MethodCode { get; set; } = string.Empty;
    public int UsefulLifeMonths { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CostBasis { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SalvageValue { get; set; }
    public int PolicyVersion { get; set; }
    public long? CostAccountId { get; set; }
    public long? AccumulatedDepreciationAccountId { get; set; }
    public long? DepreciationExpenseAccountId { get; set; }
    public long? ImpairmentExpenseAccountId { get; set; }
    public long? DisposalGainAccountId { get; set; }
    public long? DisposalLossAccountId { get; set; }
    public bool IsActive { get; set; }
}

public class AssetDepreciationRun : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly PeriodEnd { get; set; }
    [Required, MaxLength(30)] public string StateCode { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAmount { get; set; }
    public long? JournalId { get; set; }
    public long? ReversalJournalId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PostedAt { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; };
}

public class AssetDepreciationLine : BaseTenantEntity
{
    public long AssetDepreciationRunId { get; set; }
    public long AssetBookId { get; set; }
    public DateOnly PeriodEnd { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OpeningAccumulated { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ClosingAccumulated { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ClosingCarryingValue { get; set; }
    public long? JournalLineId { get; set; }
}

public class AssetImpairment : BaseTenantEntity
{
    public long AssetBookId { get; set; }
    public Guid ClientRequestId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, MaxLength(30)] public string StateCode { get; set; } = string.Empty;
    public long? JournalId { get; set; }
    public long? ReversalJournalId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; };
}

public class AssetDisposal : BaseTenantEntity
{
    public long AssetBookId { get; set; }
    public Guid ClientRequestId { get; set; }
    public DateOnly DisposalDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Proceeds { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AccumulatedDepreciation { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CarryingValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal GainLoss { get; set; }
    [Required, MaxLength(30)] public string StateCode { get; set; } = string.Empty;
    public long? JournalId { get; set; }
    public long? ReversalJournalId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}
