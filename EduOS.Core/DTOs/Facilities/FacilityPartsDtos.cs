using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Facilities;

public sealed class CreateFacilityPartsIssueDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long FromLocationId { get; set; }
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required, MinLength(1)] public IReadOnlyList<FacilityPartsIssueLineInputDto> Lines { get; set; } = Array.Empty<FacilityPartsIssueLineInputDto>();
}

public sealed class VoidFacilityPartsIssueDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}

public sealed class FacilityPartsIssueLineInputDto
{
    [Range(1, long.MaxValue)] public long InventoryItemId { get; set; }
    [Range(typeof(decimal), "0.000001", "999999999999")] public decimal Quantity { get; set; }
    [MaxLength(100)] public string? LotOrBatchNo { get; set; }
}

public sealed class FacilityPartsIssueDto
{
    public Guid Reference { get; set; }
    public string MovementNumber { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public long FromLocationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public Guid? ReversalReference { get; set; }
    public long? FinanceJournalId { get; set; }
    public Guid? FinanceReversalReference { get; set; }
    public string FinanceState { get; set; } = "NotPosted";
    public decimal GrossCost { get; set; }
    public decimal NetCost { get; set; }
    public IReadOnlyList<FacilityPartsIssueLineDto> Lines { get; set; } = Array.Empty<FacilityPartsIssueLineDto>();
}

public sealed class FacilityPartsIssueLineDto
{
    public long InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal PostedUnitCost { get; set; }
    public string LotOrBatchNo { get; set; } = string.Empty;
}
