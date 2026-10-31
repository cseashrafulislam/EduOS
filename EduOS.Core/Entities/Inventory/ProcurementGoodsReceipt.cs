using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

/// <summary>Supplier delivery evidence. Only the linked posted InventoryMovement changes stock.</summary>
public sealed class ProcurementGoodsReceipt : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long PurchaseOrderId { get; set; }
    public long CampusId { get; set; }
    public long LocationId { get; set; }
    [Required, MaxLength(25)] public string StateCode { get; set; } = "Draft";
    [Required, MaxLength(800)] public string Reason { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string CreateFingerprint { get; set; } = string.Empty;
    public Guid? SubmitRequestId { get; set; }
    [MaxLength(64)] public string? SubmitFingerprint { get; set; }
    public Guid? ReviewRequestId { get; set; }
    [MaxLength(64)] public string? ReviewFingerprint { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? SubmittedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    [MaxLength(800)] public string? ReviewReason { get; set; }
    [MaxLength(25)] public string? ValuationMode { get; set; }
    public long? PostedMovementId { get; set; }
}

public sealed class ProcurementGoodsReceiptLine : BaseTenantEntity
{
    public long GoodsReceiptId { get; set; }
    public int LineNo { get; set; }
    public long PurchaseOrderLineId { get; set; }
    public long InventoryItemId { get; set; }
    [Required, MaxLength(100)] public string LotOrBatchNo { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,6)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal ApprovedPoQuantity { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal ApprovedPoUnitPrice { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? PostedUnitCost { get; set; }
    [MaxLength(800)] public string? ValuationReason { get; set; }
}
