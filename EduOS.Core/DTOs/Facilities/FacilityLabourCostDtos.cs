using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Facilities;

public sealed class CreateFacilityLabourAllocationDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long FacilityWorkOrderId { get; set; }
    [Range(1, long.MaxValue)] public long PayrollEmployeeId { get; set; }
    [Range(1, 60000)] public int ActualWorkMinutes { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Evidence { get; set; } = string.Empty;
}
public sealed class ReviewFacilityLabourAllocationDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Reason { get; set; } = string.Empty;
}
public sealed class FacilityLabourAllocationDto
{
    public Guid Reference { get; set; }
    public long FacilityWorkOrderId { get; set; }
    public long CampusId { get; set; }
    public long PayrollEmployeeId { get; set; }
    public long EmployeeId { get; set; }
    public long PayrollRunId { get; set; }
    public int ActualWorkMinutes { get; set; }
    public int StandardPayrollMinutesSnapshot { get; set; }
    public decimal PayrollGrossSnapshotBdt { get; set; }
    public decimal AllocatedAmountBdt { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string? ReviewReason { get; set; }
    public bool PayrollSourceReversed { get; set; }
}
