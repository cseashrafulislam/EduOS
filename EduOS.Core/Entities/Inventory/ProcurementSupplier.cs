using EduOS.Core.Entities.Base;
namespace EduOS.Core.Entities.Inventory;
public sealed class ProcurementSupplier : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    [Required, MaxLength(40)] public string SupplierCode { get; set; } = "";
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(100)] public string? TaxRegistrationNo { get; set; }
    [Required, MaxLength(20)] public string StateCode { get; set; } = "Draft";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
}
