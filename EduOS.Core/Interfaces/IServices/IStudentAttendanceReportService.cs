using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentAttendanceReportService
{
    Task<ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>> GetSubjectChoicesAsync(
        long academicBatchId, string? search, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<StudentAttendanceDailySummaryDto>>> GetDailySummaryAsync(
        StudentAttendanceReportQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentAttendanceReportRowDto>>> GetAsync(
        StudentAttendanceReportQueryDto query, CancellationToken cancellationToken = default);
}
