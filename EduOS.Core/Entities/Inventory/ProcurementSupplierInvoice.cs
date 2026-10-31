using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

/// <summary>Immutable supplier bill and receipt-line evidence. The linked Journal alone owns GL balances.</summary>
public sealed class ProcurementSupplierInvoice : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long PurchaseOrderId { get; set; }
    public long SupplierId { get; set; }
    public long CampusId { get; set; }
    [Required, MaxLength(100)] public string SupplierInvoiceNumber { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string NormalizedSupplierInvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    [Required, MaxLength(25)] public string StateCode { get; set; } = "Draft";
    [Required, MaxLength(25)] public string PriceMode { get; set; } = "Automatic";
    [Required, MaxLength(800)] public string Reason { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string CreateFingerprint { get; set; } = string.Empty;
    public Guid? SubmitRequestId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? SubmittedByUserId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    [MaxLength(64)] public string? ReviewFingerprint { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    [MaxLength(800)] public string? ReviewReason { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal StockValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PriceVariance { get; set; }
    public long? JournalId { get; set; }
    public long? ReversalJournalId { get; set; }
}

public sealed class ProcurementSupplierInvoiceLine : BaseTenantEntity
{
    public long SupplierInvoiceId { get; set; }
    public int LineNo { get; set; }
    public long GoodsReceiptLineId { get; set; }
    public long PurchaseOrderLineId { get; set; }
    public long InventoryItemId { get; set; }
    [Required, MaxLength(100)] public string LotOrBatchNo { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,6)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal ReceivedQuantitySnapshot { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal PoUnitPriceSnapshot { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal StockUnitCostSnapshot { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal InvoiceUnitPrice { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal InvoiceAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal StockValue { get; set; }
    [MaxLength(800)] public string? VarianceReason { get; set; }
}
