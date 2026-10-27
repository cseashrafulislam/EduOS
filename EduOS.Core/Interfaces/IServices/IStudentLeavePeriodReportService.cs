using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentLeavePeriodReportService
{
    Task<ApiResponse<PagedResult<StudentLeavePeriodReportRowDto>>> GetAsync(
        StudentLeavePeriodReportQueryDto query, CancellationToken cancellationToken = default);
}
