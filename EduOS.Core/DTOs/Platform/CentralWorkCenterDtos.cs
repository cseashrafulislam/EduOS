using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Platform;

/// <summary>Read-only references to actions owned by their original modules.</summary>
public sealed class CentralWorkItemDto
{
    public string Source { get; set; } = string.Empty;
    public long SourceId { get; set; }
    // Null identifies an institution-wide item, never a synthetic campus.
    public long? CampusId { get; set; }
    public Guid? SourceReference { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
    public string WorkspaceUrl { get; set; } = string.Empty;
}

public sealed class CentralWorkCenterPageDto
{
    public PagedResult<CentralWorkItemDto> Pending { get; set; } = new();
    public bool InventoryIncluded { get; set; }
    public bool FacilitiesIncluded { get; set; }
    public bool FinanceIncluded { get; set; }
}
