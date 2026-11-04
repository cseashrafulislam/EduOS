using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Facilities;

/// <summary>
/// Management-only historical allocation of a canonical posted PayrollEmployee
/// gross amount to a verified Facilities work order. NEVER a new GL expense.
/// </summary>
public sealed class FacilityLabourCostAllocation : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long FacilityWorkOrderId { get; set; }
    public long CampusId { get; set; }
    public long PayrollEmployeeId { get; set; }
    public long EmployeeId { get; set; }
    public long PayrollRunId { get; set; }
    public int ActualWorkMinutes { get; set; }
    public int StandardPayrollMinutesSnapshot { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PayrollGrossSnapshotBdt { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AllocatedAmountBdt { get; set; }
    [Required, MaxLength(15)] public string StateCode { get; set; } = "Submitted";
    [Required, MaxLength(800)] public string Evidence { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    [MaxLength(64)] public string? ReviewFingerprint { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(800)] public string? ReviewReason { get; set; }
}
