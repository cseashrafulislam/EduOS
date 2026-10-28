using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentAttendanceReportService
{
    Task<ApiResponse<StudentAttendanceRosterDto>> GetFinalizedSessionRosterAsync(
        long sessionId, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceRosterDto>> GetFilteredFinalizedSessionRosterAsync(
        long sessionId, string? search, AttendanceState? state, int page = 1,
        int pageSize = 50, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>> GetSubjectChoicesAsync(
        long academicBatchId, string? search, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentAttendanceSubjectSummaryDto>>> GetSubjectSummaryAsync(
        StudentAttendanceReportQueryDto query, string? search, string? sort,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceSessionOverviewDto>> GetSessionOverviewAsync(
        StudentAttendanceReportQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentAttendanceSessionRegisterDto>>> GetSessionRegisterAsync(
        StudentAttendanceReportQueryDto query, string? status,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAttendanceRiskSummaryDto>> GetRiskSummaryAsync(
        StudentAttendanceReportQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<StudentAttendanceDailySummaryDto>>> GetDailySummaryAsync(
        StudentAttendanceReportQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentAttendanceReportRowDto>>> GetAsync(
        StudentAttendanceReportQueryDto query, CancellationToken cancellationToken = default);
}
