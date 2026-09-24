using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
namespace EduOS.Core.Interfaces.IServices;
public interface IHrAdminService
{
 Task<ApiResponse<PagedResult<HrEmployeeRowDto>>> GetEmployeesAsync(HrEmployeeQueryDto request,CancellationToken cancellationToken=default);
 Task<ApiResponse<PagedResult<HrLeaveRowDto>>> GetEmployeeLeavesAsync(HrLeaveQueryDto request,CancellationToken cancellationToken=default);
 Task<ApiResponse<bool>> ReviewEmployeeLeaveAsync(ReviewEmployeeLeaveDto request,CancellationToken cancellationToken=default);
}
