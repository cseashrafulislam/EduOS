using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Inventory;

/// <summary>
/// LineNo refers to the original immutable requested reservation line.
/// A stable ClientRequestId makes a posted partial issue retry-safe.
/// </summary>
public sealed class FulfillInventoryReservedIssueRequestDto
{
    public Guid ClientRequestId { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(100)]
    public IReadOnlyList<FulfillInventoryReservedIssueLineDto> Lines { get; set; }
        = Array.Empty<FulfillInventoryReservedIssueLineDto>();
}

public sealed class FulfillInventoryReservedIssueLineDto
{
    [Range(1, int.MaxValue)]
    public int LineNo { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999999")]
    public decimal Quantity { get; set; }
}
