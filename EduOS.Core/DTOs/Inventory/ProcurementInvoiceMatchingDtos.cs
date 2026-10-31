namespace EduOS.Core.DTOs.Inventory;

/// <summary>A non-posting supplier invoice preview. No invoice, payable or GL entry is created.</summary>
public sealed class PreviewProcurementInvoiceRequest
{
    public long PurchaseOrderId { get; set; }
    public string PriceMode { get; set; } = "Automatic";
    public List<PreviewProcurementInvoiceLineRequest> Lines { get; set; } = [];
}

public sealed class PreviewProcurementInvoiceLineRequest
{
    public Guid GoodsReceiptReference { get; set; }
    public int ReceiptLineNo { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>ManualFinal = final price; AutoPlusManual = signed delta to approved PO price.</summary>
    public decimal Amount { get; set; }
    public string? VarianceReason { get; set; }
}

public sealed class PreviewProcurementInvoiceLineDto
{
    public Guid GoodsReceiptReference { get; set; }
    public int ReceiptLineNo { get; set; }
    public long PurchaseOrderLineId { get; set; }
    public long InventoryItemId { get; set; }
    public string LotOrBatchNo { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal ApprovedPoUnitPrice { get; set; }
    public decimal PostedStockUnitCost { get; set; }
    public decimal ProposedInvoiceUnitPrice { get; set; }
    public decimal ProposedAmount { get; set; }
    public decimal DifferenceFromStockValue { get; set; }
    public bool HasPriceVariance { get; set; }
    public string? VarianceReason { get; set; }
}

public sealed class PreviewProcurementInvoiceDto
{
    public long PurchaseOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long SupplierId { get; set; }
    public string PriceMode { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "BDT";
    public decimal ProposedAmount { get; set; }
    public decimal DifferenceFromStockValue { get; set; }
    public bool RequiresIndependentFinanceReview { get; set; }
    public string StatusCode { get; set; } = "PreviewOnly";
    public List<PreviewProcurementInvoiceLineDto> Lines { get; set; } = [];
}
