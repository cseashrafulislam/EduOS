using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Inventory;
public sealed class InventoryLocationDto
{
    public long Id { get; set; }
    public long? CampusId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class SaveInventoryLocationRequestDto
{
    public long? CampusId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
public sealed class InventoryItemDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string UnitCode { get; set; } = "Each";
    public string? CategoryCode { get; set; }
    public bool IsStockTracked { get; set; }
    public bool IsAssetTracked { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class SaveInventoryItemRequestDto
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string UnitCode { get; set; } = "Each";
    [MaxLength(100)] public string? CategoryCode { get; set; }
    public bool IsStockTracked { get; set; } = true;
    public bool IsAssetTracked { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
public sealed class InventoryMovementDto
{
    public Guid Reference { get; set; }
    public string MovementNumber { get; set; } = string.Empty;
    public string MovementTypeCode { get; set; } = string.Empty;
    public DateTime MovementAt { get; set; }
    public long? FromLocationId { get; set; }
    public long? ToLocationId { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public DateTime? PostedAt { get; set; }
    public IReadOnlyList<InventoryMovementLineDto> Lines { get; set; } = Array.Empty<InventoryMovementLineDto>();
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class InventoryMovementLineDto
{
    public long InventoryItemId { get; set; }
    public int LineNo { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? LotOrBatchNo { get; set; }
}
public sealed class CreateInventoryMovementRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(30)] public string MovementTypeCode { get; set; } = string.Empty;
    public long? FromLocationId { get; set; }
    public long? ToLocationId { get; set; }
    [MaxLength(100)] public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    [Required, MinLength(1)] public IReadOnlyList<CreateInventoryMovementLineRequestDto> Lines { get; set; } = Array.Empty<CreateInventoryMovementLineRequestDto>();
}
public sealed class CreateInventoryMovementLineRequestDto
{
    [Range(1, long.MaxValue)] public long InventoryItemId { get; set; }
    [Range(typeof(decimal), "0.000001", "999999999999")] public decimal Quantity { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal UnitCost { get; set; }
    [MaxLength(100)] public string? LotOrBatchNo { get; set; }
}
public sealed class PostInventoryMovementRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
public sealed class ReverseInventoryMovementRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
public sealed class InventoryBalanceDto
{
    public long InventoryItemId { get; set; }
    public long InventoryLocationId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal ValuationAmount { get; set; }
    public DateTime? AsOfUtc { get; set; }
}
public sealed class AssetCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class SaveAssetCategoryRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
public sealed class AssetDto
{
    public Guid Reference { get; set; }
    public long AssetCategoryId { get; set; }
    public long? InventoryItemId { get; set; }
    public long? InventoryLocationId { get; set; }
    public string AssetTag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class SaveAssetRequestDto
{
    [Range(1, long.MaxValue)] public long AssetCategoryId { get; set; }
    public long? InventoryItemId { get; set; }
    public long? CampusId { get; set; }
    public long? InventoryLocationId { get; set; }
    [Required, MaxLength(100)] public string AssetTag { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal? PurchaseCost { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
public sealed class AssetAssignmentDto
{
    public long Id { get; set; }
    public Guid AssetReference { get; set; }
    public Guid? EmployeeReference { get; set; }
    public Guid? StudentReference { get; set; }
    public long? OrganizationUnitId { get; set; }
    public DateOnly AssignedOn { get; set; }
    public DateOnly? ReturnedOn { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class AssignAssetRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid AssetReference { get; set; }
    public Guid? EmployeeReference { get; set; }
    public Guid? StudentReference { get; set; }
    public long? OrganizationUnitId { get; set; }
    public DateOnly AssignedOn { get; set; }
}
public sealed class ReturnAssetRequestDto
{
    public DateOnly ReturnedOn { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
public sealed class AssetMaintenanceDto
{
    public long Id { get; set; }
    public Guid AssetReference { get; set; }
    public DateOnly MaintenanceDate { get; set; }
    public string WorkDescription { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public DateOnly? NextMaintenanceDate { get; set; }
}
public sealed class RecordAssetMaintenanceRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid AssetReference { get; set; }
    public DateOnly MaintenanceDate { get; set; }
    [Required, MaxLength(200)] public string WorkDescription { get; set; } = string.Empty;
    [Range(typeof(decimal), "0", "999999999999")] public decimal Cost { get; set; }
    [MaxLength(200)] public string? ServiceProvider { get; set; }
    public DateOnly? NextMaintenanceDate { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
}
