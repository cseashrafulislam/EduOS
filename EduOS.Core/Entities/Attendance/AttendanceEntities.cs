using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Attendance;

public class StudentAttendance : BaseTenantEntity
{
    public long AttendanceSessionId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public AttendanceState State { get; set; } = AttendanceState.Present;
    public TimeOnly? CheckInTime { get; set; }
    public TimeOnly? CheckOutTime { get; set; }
    public long? RecordedByUserId { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(500)] public string? Remarks { get; set; }
}

public class EmployeeAttendance : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public AttendanceState State { get; set; } = AttendanceState.Present;
    public TimeOnly? InTime { get; set; }
    public TimeOnly? OutTime { get; set; }
    [MaxLength(50)] public string? SourceCode { get; set; }
    public long? RecordedByUserId { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "decimal(18,2)")] public decimal? OvertimeHours { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}

public class LeaveType : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public int MaxDaysPerYear { get; set; }
    public bool IsPaid { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public class EmployeeLeaveApplication : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalDays { get; set; }
    [Required, MaxLength(2000)] public string Reason { get; set; } = string.Empty;
    public long? AttachmentFileId { get; set; }
    public LeaveState State { get; set; } = LeaveState.Draft;
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
}

public class StudentLeaveApplication : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Required, MaxLength(2000)] public string Reason { get; set; } = string.Empty;
    public long? AttachmentFileId { get; set; }
    public LeaveState State { get; set; } = LeaveState.Draft;
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
}

public class EmployeeLeaveEntitlement : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public int Year { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal EntitledDays { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CarriedForwardDays { get; set; }
}

public class EmployeeLeaveAdjustment : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public int Year { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Days { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    public long ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
}

public class StudentAttendanceAdjustment : BaseTenantEntity
{
    public long StudentAttendanceId { get; set; }
    public AttendanceState PreviousState { get; set; }
    public AttendanceState NewState { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    public long ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

public class EmployeeAttendanceAdjustment : BaseTenantEntity
{
    public long EmployeeAttendanceId { get; set; }
    public AttendanceState PreviousState { get; set; }
    public AttendanceState NewState { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    public long ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
