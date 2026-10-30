using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Inventory;

/// <summary>
/// A reasoned, maker-checker stock correction. This is a canonical movement draft,
/// not a separate stock balance or a substitute for a physical count sheet.
/// </summary>
public sealed class CreateInventoryStockAdjustmentRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long LocationId { get; set; }
    [Range(1, long.MaxValue)] public long InventoryItemId { get; set; }
    [Required] public string Direction { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.000001", "999999999999")] public decimal Quantity { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal UnitCost { get; set; }
    [MaxLength(100)] public string? LotOrBatchNo { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Reason { get; set; } = string.Empty;
}
