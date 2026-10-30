namespace EduOS.Core.DTOs.Inventory;

/// <summary>
/// Current posted stock versus active reservation holds for one authorized store,
/// item and lot. Each row uses that item's base unit; never total unlike units.
/// </summary>
public sealed class InventoryReservationCoverageRowDto
{
    public long InventoryItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public string LotOrBatchNo { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal OverdueReservedQuantity { get; set; }
    public decimal ShortfallQuantity { get; set; }
    public int ActiveHoldLines { get; set; }
    public DateTime OldestHoldUtc { get; set; }
    public int OldestHoldAgeDays { get; set; }
    public bool IsOverdue { get; set; }
    public bool HasShortfall { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
}
