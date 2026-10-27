namespace EduOS.Core.DTOs.Inventory;

/// <summary>Posted stock effects at one authorized location; recorded costs are not a GL valuation.</summary>
public sealed class InventoryItemHistoryRowDto
{
    public Guid MovementReference { get; set; }
    public string MovementNumber { get; set; } = string.Empty;
    public string MovementTypeCode { get; set; } = string.Empty;
    public DateTime PostedAtUtc { get; set; }
    public string? LotOrBatchNo { get; set; }
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal RecordedCostIn { get; set; }
    public decimal RecordedCostOut { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public bool IsReversal { get; set; }
}

public sealed class InventoryItemHistoryReportDto
{
    public long InventoryItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public long InventoryLocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? LotOrBatchNo { get; set; }
    public decimal OpeningQuantity { get; set; }
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal ClosingQuantity { get; set; }
    public decimal OpeningRecordedCost { get; set; }
    public decimal RecordedCostIn { get; set; }
    public decimal RecordedCostOut { get; set; }
    public decimal ClosingRecordedCost { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<InventoryItemHistoryRowDto> Items { get; set; } = Array.Empty<InventoryItemHistoryRowDto>();
}
