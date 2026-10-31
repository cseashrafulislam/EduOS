using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

// The snapshot and audit evidence are append-only after submission.
public sealed class InventoryPhysicalCount : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long LocationId { get; set; }
    public bool Blind { get; set; } = true;
    [Required, MaxLength(30)] public string StateCode { get; set; } = "Draft";
    [Required, MaxLength(800)] public string Reason { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string StartFingerprint { get; set; } = string.Empty;
    [MaxLength(64)] public string? SubmitFingerprint { get; set; }
    [MaxLength(64)] public string? ReviewFingerprint { get; set; }
    public Guid? SubmitRequestId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public DateTime SnapshotAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? SubmittedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    [MaxLength(30)] public string? VarianceMode { get; set; }
    [MaxLength(800)] public string? ReviewReason { get; set; }
}

public sealed class InventoryPhysicalCountLine : BaseTenantEntity
{
    public long InventoryPhysicalCountId { get; set; }
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    [Required, MaxLength(100)] public string LotOrBatchNo { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,6)")] public decimal BookQuantity { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal BookValue { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal ReservedQuantity { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal? CountedQuantity { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal? PostedVariance { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? PostedUnitCost { get; set; }
    [MaxLength(800)] public string? CorrectionReason { get; set; }
    public long? PostedMovementId { get; set; }
}
