namespace EduOS.Core.DTOs.Inventory;

public sealed class CreateProcurementGoodsReceiptRequest
{
    public Guid ClientRequestId { get; set; }
    public long PurchaseOrderId { get; set; }
    public long LocationId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<ProcurementGoodsReceiptLineInput> Lines { get; set; } = [];
}

public sealed class ProcurementGoodsReceiptLineInput
{
    public long PurchaseOrderLineId { get; set; }
    public decimal Quantity { get; set; }
    public string? LotOrBatchNo { get; set; }
}

public sealed class SubmitProcurementGoodsReceiptRequest
{
    public Guid ClientRequestId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReviewProcurementGoodsReceiptRequest
{
    public Guid ClientRequestId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string Decision { get; set; } = "Approve";
    public string ValuationMode { get; set; } = "Automatic";
    public string Reason { get; set; } = string.Empty;
    public List<ProcurementGoodsReceiptValuationInput> Valuations { get; set; } = [];
}

public sealed class ProcurementGoodsReceiptValuationInput
{
    public int LineNo { get; set; }
    // ManualFinal = selected purchase unit cost; AutoPlusManual = signed adjustment to the PO price.
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
}

public sealed class ProcurementGoodsReceiptLineDto
{
    public int LineNo { get; set; }
    public long PurchaseOrderLineId { get; set; }
    public long InventoryItemId { get; set; }
    public string LotOrBatchNo { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ApprovedPoQuantity { get; set; }
    public decimal ApprovedPoUnitPrice { get; set; }
    public decimal? PostedUnitCost { get; set; }
    public string? ValuationReason { get; set; }
}

public sealed class ProcurementGoodsReceiptDto
{
    public Guid Reference { get; set; }
    public long PurchaseOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long LocationId { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long? CreatedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    public string? ReviewReason { get; set; }
    public string? ValuationMode { get; set; }
    public Guid? PostedMovementReference { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<ProcurementGoodsReceiptLineDto> Lines { get; set; } = [];
}
