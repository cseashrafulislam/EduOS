namespace EduOS.Core.DTOs.Inventory;

public sealed class CreateProcurementPurchaseOrderRequest
{
    public Guid ClientRequestId { get; set; }
    public long RequisitionId { get; set; }
    public long SupplierId { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
}

public sealed class ProcurementPurchaseOrderDraftLineInput
{
    public long RequisitionLineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
public sealed class SaveProcurementPurchaseOrderDraftRequest
{
    public string RowVersion { get; set; } = string.Empty;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<ProcurementPurchaseOrderDraftLineInput> Lines { get; set; } = new();
}

public sealed class ProcurementPurchaseOrderDecisionRequest
{
    public string RowVersion { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class ProcurementPurchaseOrderLineDto
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public long RequisitionLineId { get; set; }
    public long InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class ProcurementPurchaseOrderDto
{
    public long Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long RequisitionId { get; set; }
    public long SupplierId { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public string StateCode { get; set; } = string.Empty;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    public string? DecisionReason { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public List<ProcurementPurchaseOrderLineDto> Lines { get; set; } = new();
}

/// <summary>Tenant/campus-scoped, sanitized audit history; never expose raw audit JSON.</summary>
public sealed class ProcurementPurchaseOrderHistoryDto
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public long? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? PreviousState { get; set; }
    public string? State { get; set; }
    public string? Reason { get; set; }
}

public sealed class ProcurementPurchaseOrderCommitmentDto
{
    public long Id { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "BDT";
    public DateTime? ExpectedDeliveryDate { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime AgingDate { get; set; }
    public int DaysOverdue { get; set; }
    public string DeliverySchedule { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public DateTime? SupplierConfirmedAt { get; set; }
    public DateTime? SupplierPromisedDeliveryDate { get; set; }
    public string? SupplierConfirmationReference { get; set; }
}
