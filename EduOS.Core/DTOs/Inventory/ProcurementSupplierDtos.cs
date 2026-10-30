namespace EduOS.Core.DTOs.Inventory;
public sealed class SaveProcurementSupplierRequest
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public string SupplierCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? TaxRegistrationNo { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class ProcurementSupplierDecisionRequest
{
    public string RowVersion { get; set; } = "";
    public string? Reason { get; set; }
}
public sealed class ProcurementSupplierDto
{
    public long Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public string SupplierCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? TaxRegistrationNo { get; set; }
    public string StateCode { get; set; } = "";
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    public string? DecisionReason { get; set; }
    public string RowVersion { get; set; } = "";
}
