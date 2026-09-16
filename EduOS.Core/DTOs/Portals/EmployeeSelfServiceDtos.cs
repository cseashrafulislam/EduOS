namespace EduOS.Core.DTOs.Portals;

public sealed class EmployeePortalProfileDto
{
    public Guid Reference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public long DesignationId { get; set; }
    public long? DepartmentId { get; set; }
    public DateTime JoiningDate { get; set; }
    public decimal Salary { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Qualification { get; set; }
    public string? Experience { get; set; }
    public bool IsTeacher { get; set; }
}

public sealed class EmployeePortalAttendanceDto
{
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public TimeSpan? InTime { get; set; }
    public TimeSpan? OutTime { get; set; }
    public decimal? OvertimeHours { get; set; }
    public string? Remarks { get; set; }
}

public sealed class EmployeePortalLeaveDto
{
    public long Id { get; set; }
    public string LeaveType { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int TotalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

public sealed class EmployeePortalLeaveApplyDto
{
    public long LeaveTypeId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}
