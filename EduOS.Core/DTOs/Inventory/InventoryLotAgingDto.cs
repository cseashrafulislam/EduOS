namespace EduOS.Core.DTOs.Inventory;

/// <summary>
/// Posted-ledger lot position and time since the most recent inbound at a store.
/// This is NOT FIFO stock age, expiry age, or an approved financial valuation.
/// </summary>
public sealed class InventoryLotAgingRowDto
{
    public long InventoryItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public string LotOrBatchNo { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal RecordedCostBalance { get; set; }
    public DateTime? LatestInboundUtc { get; set; }
    public int? DaysSinceLatestInbound { get; set; }
    public bool HasIntegrityException { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
}
