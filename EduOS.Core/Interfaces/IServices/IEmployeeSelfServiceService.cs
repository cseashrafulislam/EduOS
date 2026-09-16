using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;

namespace EduOS.Core.Interfaces.IServices;

public interface IEmployeeSelfServiceService
{
    Task<ApiResponse<EmployeePortalProfileDto>> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>> GetAttendanceAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>> GetLeaveHistoryAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeePortalLeaveDto>> ApplyLeaveAsync(EmployeePortalLeaveApplyDto request, CancellationToken cancellationToken = default);
}
