namespace EduOS.Core.DTOs.Inventory;

public sealed class ProcurementRequisitionLineInput
{
    public long InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal EstimatedUnitPrice { get; set; }
    public string? Remarks { get; set; }
}

public sealed class SaveProcurementRequisitionRequest
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? RowVersion { get; set; }
    public List<ProcurementRequisitionLineInput> Lines { get; set; } = new();
}

public sealed class ProcurementDecisionRequest
{
    public string RowVersion { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class ProcurementRequisitionLineDto
{
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal EstimatedUnitPrice { get; set; }
    public string? Remarks { get; set; }
}

public sealed class ProcurementRequisitionDto
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    public string? DecisionReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public decimal EstimatedTotal { get; set; }
    public List<ProcurementRequisitionLineDto> Lines { get; set; } = new();
}

/// <summary>Bounded, authorized history projection; never returns raw audit JSON.</summary>
public sealed class ProcurementRequisitionHistoryDto
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public long? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? PreviousState { get; set; }
    public string? State { get; set; }
    public string? Reason { get; set; }
}
