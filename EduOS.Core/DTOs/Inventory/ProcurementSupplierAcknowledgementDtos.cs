namespace EduOS.Core.DTOs.Inventory;

public sealed class SaveProcurementSupplierAcknowledgementRequest
{
    public Guid ClientRequestId { get; set; }
    public long PurchaseOrderId { get; set; }
    public string SupplierReference { get; set; } = string.Empty;
    public DateTime ResponseDate { get; set; }
    public DateTime PromisedDeliveryDate { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class ProcurementSupplierAcknowledgementDecisionRequest
{
    public string RowVersion { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
public sealed class ProcurementSupplierAcknowledgementDto
{
    public long Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long PurchaseOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public string SupplierReference { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public DateTime ResponseDate { get; set; }
    public DateTime PromisedDeliveryDate { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    public string? DecisionReason { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
