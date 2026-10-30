using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

/// <summary>Approved requisition converted into a campus-owned supplier commitment; never a stock or GL ledger.</summary>
public sealed class ProcurementPurchaseOrder : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(50)] public string OrderNumber { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long RequisitionId { get; set; }
    public long SupplierId { get; set; }
    [Required, MaxLength(3)] public string CurrencyCode { get; set; } = "BDT";
    [Required, MaxLength(20)] public string StateCode { get; set; } = "Draft";
    public DateTime? ExpectedDeliveryDate { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
}

public sealed class ProcurementPurchaseOrderLine : BaseTenantEntity
{
    public long PurchaseOrderId { get; set; }
    public long RequisitionLineId { get; set; }
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal UnitPrice { get; set; }
}
