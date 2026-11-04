using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Facilities;

/// <summary>
/// Source-linked non-stock supplier bill. The existing Finance Journal owns GL.
/// A booking or service certificate alone never creates a payable.
/// </summary>
public sealed class FacilityVendorServiceBill : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long FacilityWorkOrderId { get; set; }
    public long CampusId { get; set; }
    public long SupplierId { get; set; }
    [Required, MaxLength(100)] public string SupplierInvoiceNumber { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string NormalizedSupplierInvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AmountBdt { get; set; }
    [Required, MaxLength(20)] public string StateCode { get; set; } = "Draft";
    [Required, MaxLength(800)] public string Reason { get; set; } = string.Empty;
    public long PreparedByUserId { get; set; }
    public Guid? SubmitRequestId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    [MaxLength(800)] public string? ReviewReason { get; set; }
    public long? JournalId { get; set; }
}
