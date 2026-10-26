namespace EduOS.Core.DTOs.Inventory;

public sealed class InventoryStockPositionRowDto
{
    public long InventoryItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public decimal ReorderLevel { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal RecordedCostBalance { get; set; }
    public int ActiveLotCount { get; set; }
    public bool IsBelowReorder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class InventoryStockPositionReportDto
{
    public long InventoryLocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
    public string? Search { get; set; }
    public bool IncludeZeroStock { get; set; }
    public int CatalogItemCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<InventoryStockPositionRowDto> Items { get; set; } = Array.Empty<InventoryStockPositionRowDto>();
}
