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
    public TimeOnly? CheckOutTime { get; set; }
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

public sealed class EnsureStudentAttendanceSessionDto
{
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    [Range(1, long.MaxValue)] public long? SubjectOfferingId { get; set; }
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

public sealed class StudentAttendanceRosterQueryDto
{
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public long? SubjectOfferingId { get; set; }
}

public sealed class StudentAttendanceSummaryDto
{
    public int TotalStudents { get; set; }
    public int Marked { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int Leave { get; set; }
    public int Unmarked { get; set; }
}

public sealed class StudentAttendanceRosterDto
{
    public long AttendanceSessionId { get; set; }
    public string SessionRowVersion { get; set; } = string.Empty;
    public long AcademicBatchId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public StudentAttendanceSummaryDto Summary { get; set; } = new();
    public IReadOnlyList<StudentAttendanceDto> Students { get; set; } = Array.Empty<StudentAttendanceDto>();
}

public sealed class AttendanceCorrectionRequestDto
{
    public AttendanceState NewState { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class StudentAttendanceAdjustmentDto
{
    public long Id { get; set; }
    public long StudentAttendanceId { get; set; }
    public AttendanceState PreviousState { get; set; }
    public AttendanceState NewState { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}

public sealed class EmployeeAttendanceAdjustmentDto
{
    public long Id { get; set; }
    public long EmployeeAttendanceId { get; set; }
    public AttendanceState PreviousState { get; set; }
    public AttendanceState NewState { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}

public sealed class StudentLeaveApplicationDto
{
    public long Id { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long? AttachmentFileId { get; set; }
    public LeaveState State { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateStudentLeaveApplicationRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Required, MaxLength(2000)] public string Reason { get; set; } = string.Empty;
    public long? AttachmentFileId { get; set; }
}

public sealed class ReviewStudentLeaveRequestDto
{
    public LeaveState State { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class EmployeeLeaveEntitlementDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public long LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal EntitledDays { get; set; }
    public decimal CarriedForwardDays { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveEmployeeLeaveEntitlementRequestDto
{
    public Guid EmployeeReference { get; set; }
    [Range(1, long.MaxValue)] public long LeaveTypeId { get; set; }
    [Range(1900, 9999)] public int Year { get; set; }
    [Range(typeof(decimal), "0", "366")] public decimal EntitledDays { get; set; }
    [Range(typeof(decimal), "0", "366")] public decimal CarriedForwardDays { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class EmployeeLeaveAdjustmentDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public long LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; }
}

public sealed class CreateEmployeeLeaveAdjustmentRequestDto
{
    public Guid EmployeeReference { get; set; }
    [Range(1, long.MaxValue)] public long LeaveTypeId { get; set; }
    [Range(1900, 9999)] public int Year { get; set; }
    [Range(typeof(decimal), "-366", "366")] public decimal Days { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}
