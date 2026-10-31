using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

/// <summary>Audited supplier confirmation evidence for an approved PO; not a stock or Finance posting.</summary>
public sealed class ProcurementSupplierAcknowledgement : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long PurchaseOrderId { get; set; }
    public long SupplierId { get; set; }
    [Required, MaxLength(80)] public string SupplierReference { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string StateCode { get; set; } = "Draft";
    public DateTime ResponseDate { get; set; }
    public DateTime PromisedDeliveryDate { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
}
