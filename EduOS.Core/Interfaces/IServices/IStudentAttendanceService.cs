using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentAttendanceService
{
    Task<ApiResponse<PagedResult<StudentAttendanceBatchChoiceDto>>> GetBatchChoicesAsync(
        DateOnly? attendanceDate, string? search, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceRosterDto>> GetRosterAsync(StudentAttendanceRosterQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceRosterDto>> EnsureSessionAsync(EnsureStudentAttendanceSessionDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceRosterDto>> SaveAsync(SaveAttendanceRegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceRosterDto>> FinalizeSessionAsync(long sessionId, string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceDto>> CorrectAttendanceAsync(long attendanceId, AttendanceCorrectionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentAttendanceAdjustmentDto>>> GetCorrectionsAsync(long attendanceId,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}
