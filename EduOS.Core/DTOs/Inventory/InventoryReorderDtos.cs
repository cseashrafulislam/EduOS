namespace EduOS.Core.DTOs.Inventory;

public sealed class InventoryReorderRowDto
{
    public long InventoryItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public decimal ReorderLevel { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal Shortfall { get; set; }
}
public sealed class InventoryReorderReportDto
{
    public long InventoryLocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
    public int MonitoredItemCount { get; set; }
    public IReadOnlyList<InventoryReorderRowDto> Alerts { get; set; } = Array.Empty<InventoryReorderRowDto>();
}
