using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Facilities;

public sealed class FacilityPartsFinanceRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long AccountingPeriodId { get; set; }
    public DateOnly JournalDate { get; set; }
    [Range(1, long.MaxValue)] public long RepairsExpenseAccountId { get; set; }
    [Range(1, long.MaxValue)] public long InventoryAssetAccountId { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Reason { get; set; } = string.Empty;
}

public sealed class FacilityPartsFinanceReversalRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long AccountingPeriodId { get; set; }
    public DateOnly JournalDate { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Reason { get; set; } = string.Empty;
}

public sealed class FacilityPartsFinanceReviewDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
}

public sealed class FacilityPartsFinanceRecordDto
{
    public Guid JournalReference { get; set; }
    public Guid MovementReference { get; set; }
    public Guid? StockReversalReference { get; set; }
    public Guid? FinanceReversalReference { get; set; }
    public long CampusId { get; set; }
    public long FacilityRequestId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ReversalState { get; set; } = string.Empty;
    public decimal AmountBdt { get; set; }
    public long RepairsExpenseAccountId { get; set; }
    public long InventoryAssetAccountId { get; set; }
    public DateOnly JournalDate { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string? ReversalRowVersion { get; set; }
}

public sealed class FacilityPartsFinanceCandidateDto
{
    public Guid MovementReference { get; set; }
    public string MovementNumber { get; set; } = string.Empty;
    public long FacilityRequestId { get; set; }
    public long CampusId { get; set; }
    public decimal StockCostBdt { get; set; }
    public string StockState { get; set; } = string.Empty;
    public string FinanceState { get; set; } = string.Empty;
}

public sealed class FacilityPartsFinanceOptionDto
{
    public long Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public sealed class FacilityPartsFinanceOptionsDto
{
    public IReadOnlyList<FacilityPartsFinanceOptionDto> Periods { get; set; } = Array.Empty<FacilityPartsFinanceOptionDto>();
    public IReadOnlyList<FacilityPartsFinanceOptionDto> Accounts { get; set; } = Array.Empty<FacilityPartsFinanceOptionDto>();
    public bool MoreAccountsAvailable { get; set; }
}
