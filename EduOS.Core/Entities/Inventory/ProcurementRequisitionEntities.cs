using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

/// <summary>Campus-owned request for stock-tracked items. Approval is not a stock or GL posting.</summary>
public sealed class ProcurementRequisition : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(50)] public string RequestNumber { get; set; } = string.Empty;
    public long CampusId { get; set; }
    [Required, MaxLength(500)] public string Purpose { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string StateCode { get; set; } = "Draft";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
}

public sealed class ProcurementRequisitionLine : BaseTenantEntity
{
    public long RequisitionId { get; set; }
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal EstimatedUnitPrice { get; set; }
    [MaxLength(300)] public string? Remarks { get; set; }
}
