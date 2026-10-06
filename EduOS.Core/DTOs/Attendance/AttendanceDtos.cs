namespace EduOS.Core.DTOs.Attendance;

public class StudentAttendanceDto
{
    public long Id { get; set; }
    public long AttendanceSessionId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public AttendanceState State { get; set; }
    public TimeOnly? CheckInTime { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveStudentAttendanceRequestDto
{
    public Guid StudentEnrollmentReference { get; set; }
    public AttendanceState State { get; set; } = AttendanceState.Present;
    public TimeOnly? CheckInTime { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    public string? RowVersion { get; set; }
}

public class SaveAttendanceRegisterRequestDto
{
    public long AttendanceSessionId { get; set; }
    public IReadOnlyList<SaveStudentAttendanceRequestDto> Students { get; set; } = Array.Empty<SaveStudentAttendanceRequestDto>();
    [Required] public string SessionRowVersion { get; set; } = string.Empty;
}

public class EmployeeAttendanceDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly AttendanceDate { get; set; }
    public AttendanceState State { get; set; }
    public TimeOnly? InTime { get; set; }
    public TimeOnly? OutTime { get; set; }
    public decimal? OvertimeHours { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveEmployeeAttendanceRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployeeReference { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public AttendanceState State { get; set; } = AttendanceState.Present;
    public TimeOnly? InTime { get; set; }
    public TimeOnly? OutTime { get; set; }
    [Range(typeof(decimal), "0", "24")] public decimal? OvertimeHours { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    public string? RowVersion { get; set; }
}

public class LeaveTypeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int MaxDaysPerYear { get; set; }
    public bool IsPaid { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveLeaveTypeRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Range(0, 366)] public int MaxDaysPerYear { get; set; }
    public bool IsPaid { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class EmployeeLeaveApplicationDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public long LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal TotalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long? AttachmentFileId { get; set; }
    public LeaveState State { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateEmployeeLeaveApplicationRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployeeReference { get; set; }
    public long LeaveTypeId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Required, MaxLength(2000)] public string Reason { get; set; } = string.Empty;
    public long? AttachmentFileId { get; set; }
}

public class ReviewEmployeeLeaveRequestDto
{
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class LeaveBalanceDto
{
    public long LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public decimal AllowedDays { get; set; }
    public decimal UsedDays { get; set; }
    public decimal RemainingDays { get; set; }
}
