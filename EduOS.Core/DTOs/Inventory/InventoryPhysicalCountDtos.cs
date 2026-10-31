namespace EduOS.Core.DTOs.Inventory;

public sealed class StartInventoryPhysicalCountRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long LocationId { get; set; }
    public bool Blind { get; set; } = true;
    public string Reason { get; set; } = string.Empty;
    public List<InventoryPhysicalCountSelectionDto> Lines { get; set; } = [];
}

public sealed class InventoryPhysicalCountSelectionDto
{
    public long InventoryItemId { get; set; }
    public string? LotOrBatchNo { get; set; }
}

public sealed class SubmitInventoryPhysicalCountRequestDto
{
    public Guid ClientRequestId { get; set; }
    public string? RowVersion { get; set; }
    public List<InventoryPhysicalCountMeasurementDto> Lines { get; set; } = [];
}

public sealed class InventoryPhysicalCountMeasurementDto
{
    public int LineNo { get; set; }
    public decimal? CountedQuantity { get; set; }
}

public sealed class ReviewInventoryPhysicalCountRequestDto
{
    public Guid ClientRequestId { get; set; }
    public string? RowVersion { get; set; }
    public string Decision { get; set; } = "Approve";
    public string VarianceMode { get; set; } = "Automatic";
    public string Reason { get; set; } = string.Empty;
    public List<InventoryPhysicalCountCorrectionDto> Corrections { get; set; } = [];
}

public sealed class InventoryPhysicalCountCorrectionDto
{
    public int LineNo { get; set; }
    // ManualFinal: absolute signed variance. AutoPlusManual: signed increment to suggested variance.
    public decimal Quantity { get; set; }
    public decimal? ReceiptUnitCost { get; set; }
    public string? Reason { get; set; }
}

public sealed class InventoryPhysicalCountDto
{
    public Guid Reference { get; set; }
    public long LocationId { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public bool Blind { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime SnapshotAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? CreatedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public string? VarianceMode { get; set; }
    public string? ReviewReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<InventoryPhysicalCountLineDto> Lines { get; set; } = [];
}

public sealed class InventoryPhysicalCountLineDto
{
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    public string LotOrBatchNo { get; set; } = string.Empty;
    // Null on blind drafts; visibility opens after the submitted count is immutable.
    public decimal? BookQuantity { get; set; }
    public decimal? BookValue { get; set; }
    public decimal? ReservedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal? SuggestedVariance { get; set; }
    public decimal? PostedVariance { get; set; }
    public decimal? PostedUnitCost { get; set; }
    public string? CorrectionReason { get; set; }
    public Guid? PostedMovementReference { get; set; }
}
