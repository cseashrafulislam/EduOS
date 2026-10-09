using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentAttendanceService
{
    Task<ApiResponse<StudentAttendanceRosterDto>> GetRosterAsync(StudentAttendanceRosterQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceRosterDto>> SaveAsync(SaveAttendanceRegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceDto>> CorrectAttendanceAsync(long attendanceId, AttendanceCorrectionRequestDto request, CancellationToken cancellationToken = default);
}
