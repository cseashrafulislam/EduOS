using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

public class InventoryLocation : BaseTenantEntity
{
    public long? CampusId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public class InventoryItem : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(50)] public string UnitCode { get; set; } = "Each";
    [MaxLength(100)] public string? CategoryCode { get; set; }
    public bool IsStockTracked { get; set; } = true;
    public bool IsAssetTracked { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;
}

public class InventoryMovement : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(50)] public string MovementNumber { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string MovementTypeCode { get; set; } = string.Empty;
    public DateTime MovementAt { get; set; } = DateTime.UtcNow;
    public long? FromLocationId { get; set; }
    public long? ToLocationId { get; set; }
    [MaxLength(100)] public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    [Required, MaxLength(30)] public string StateCode { get; set; } = "Draft";
    public long? PostedByUserId { get; set; }
    public DateTime? PostedAt { get; set; }
    public long? ReversalOfMovementId { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
}

public class InventoryMovementLine : BaseTenantEntity
{
    public long InventoryMovementId { get; set; }
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal UnitCost { get; set; }
    [MaxLength(100)] public string? LotOrBatchNo { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}

public class AssetCategory : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Asset : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long AssetCategoryId { get; set; }
    public long? InventoryItemId { get; set; }
    public long? CampusId { get; set; }
    public long? InventoryLocationId { get; set; }
    [Required, MaxLength(100)] public string AssetTag { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PurchaseCost { get; set; }
    [MaxLength(30)] public string StateCode { get; set; } = "Available";
    public bool IsActive { get; set; } = true;
}

public class AssetAssignment : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long AssetId { get; set; }
    public long? EmployeeId { get; set; }
    public long? StudentId { get; set; }
    public long? OrganizationUnitId { get; set; }
    public DateOnly AssignedOn { get; set; }
    public DateOnly? ReturnedOn { get; set; }
    [MaxLength(30)] public string StateCode { get; set; } = "Assigned";
    [MaxLength(1000)] public string? Remarks { get; set; }
}

public class AssetMaintenance : BaseTenantEntity
{
    public long AssetId { get; set; }
    public DateOnly MaintenanceDate { get; set; }
    [Required, MaxLength(200)] public string WorkDescription { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal Cost { get; set; }
    [MaxLength(200)] public string? ServiceProvider { get; set; }
    public DateOnly? NextMaintenanceDate { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
}
