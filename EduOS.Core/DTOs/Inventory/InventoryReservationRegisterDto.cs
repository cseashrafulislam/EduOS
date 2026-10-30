namespace EduOS.Core.DTOs.Inventory;

/// <summary>One active reserved item/lot line. Quantities are never summed across units.</summary>
public sealed class InventoryReservationRegisterRowDto
{
    public Guid MovementReference { get; set; }
    public string MovementNumber { get; set; } = string.Empty;
    public string MovementTypeCode { get; set; } = string.Empty;
    public long FromLocationId { get; set; }
    public long? ToLocationId { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public DateTime HeldSinceUtc { get; set; }
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public string? LotOrBatchNo { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal FulfilledQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
}
