namespace EduOS.Core.DTOs.Inventory;

public sealed class CreateProcurementSupplierInvoiceRequest
{
    public Guid ClientRequestId { get; set; }
    public long PurchaseOrderId { get; set; }
    public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string PriceMode { get; set; } = "Automatic";
    public string Reason { get; set; } = string.Empty;
    public List<PreviewProcurementInvoiceLineRequest> Lines { get; set; } = [];
}
public sealed class SubmitProcurementSupplierInvoiceRequest
{
    public Guid ClientRequestId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class ReviewProcurementSupplierInvoiceRequest
{
    public Guid ClientRequestId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string Decision { get; set; } = "Approve";
    public string Reason { get; set; } = string.Empty;
    public long AccountingPeriodId { get; set; }
    public long InventoryAccountId { get; set; }
    public long PayableAccountId { get; set; }
    public long PriceVarianceAccountId { get; set; }
}
public sealed class RequestProcurementSupplierInvoiceReversal
{
    public Guid ClientRequestId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public DateOnly ReversalDate { get; set; }
    public long AccountingPeriodId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
public sealed class ReviewProcurementSupplierInvoiceReversal
{
    public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    public string Reason { get; set; } = string.Empty;
}
public sealed class ProcurementSupplierInvoiceLineDto
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    public long PurchaseOrderLineId { get; set; }
    public long GoodsReceiptLineId { get; set; }
    public string LotOrBatchNo { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PoUnitPrice { get; set; }
    public decimal StockUnitCost { get; set; }
    public decimal InvoiceUnitPrice { get; set; }
    public decimal InvoiceAmount { get; set; }
    public decimal StockValue { get; set; }
    public string? VarianceReason { get; set; }
}
public sealed class ProcurementSupplierInvoiceDto
{
    public Guid Reference { get; set; }
    public long PurchaseOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long SupplierId { get; set; }
    public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public string PriceMode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal StockValue { get; set; }
    public decimal PriceVariance { get; set; }
    public long? CreatedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public Guid? JournalReference { get; set; }
    public Guid? ReversalJournalReference { get; set; }
    public string? ReversalJournalRowVersion { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<ProcurementSupplierInvoiceLineDto> Lines { get; set; } = [];
}
public sealed class ProcurementInvoicePostingOptionsDto
{
    public List<ProcurementInvoiceAccountOptionDto> Accounts { get; set; } = [];
    public List<ProcurementInvoicePeriodOptionDto> Periods { get; set; } = [];
}
public sealed class ProcurementInvoiceAccountOptionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
public sealed class ProcurementInvoicePeriodOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
