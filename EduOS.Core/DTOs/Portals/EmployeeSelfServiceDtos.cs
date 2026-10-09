namespace EduOS.Core.DTOs.Portals;

public sealed class EmployeePortalProfileDto
{
    public Guid Reference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public long DesignationId { get; set; }
    public long? OrganizationUnitId { get; set; }
    public DateOnly JoiningDate { get; set; }
    public string? PhotoUrl { get; set; }
    public bool CanTeach { get; set; }
    public EmployeeState State { get; set; }
}

public sealed class EmployeePortalAttendanceDto
{
    public DateOnly AttendanceDate { get; set; }
    public AttendanceState State { get; set; }
    public TimeOnly? InTime { get; set; }
    public TimeOnly? OutTime { get; set; }
    public decimal? OvertimeHours { get; set; }
    public string? Remarks { get; set; }
}

public sealed class EmployeePortalLeaveDto
{
    public long Id { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal TotalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveState State { get; set; }
    public string? ReviewNote { get; set; }
}

public sealed class EmployeePortalLeaveBalanceDto
{
    public long LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public decimal AnnualEntitlement { get; set; }
    public decimal UsedDays { get; set; }
    public decimal PendingDays { get; set; }
    public decimal RemainingDays { get; set; }
}

public sealed class EmployeePortalLeaveApplyDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long LeaveTypeId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Required, MaxLength(2000)] public string Reason { get; set; } = string.Empty;
}
