using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-scoped employee attendance, with immutable adjustment history on corrections.</summary>
public interface IEmployeeAttendanceService
{
    Task<ApiResponse<PagedResult<EmployeeAttendanceDto>>> GetAttendanceAsync(Guid? employeeReference, DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeAttendanceDto>> RecordAttendanceAsync(SaveEmployeeAttendanceRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeAttendanceDto>> CorrectAttendanceAsync(long attendanceId, AttendanceCorrectionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<EmployeeAttendanceAdjustmentDto>>> GetCorrectionsAsync(long attendanceId, CancellationToken cancellationToken = default);
}
