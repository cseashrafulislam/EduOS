using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;

namespace EduOS.Core.Interfaces.IServices;

public interface IEmployeeSelfServiceService
{
    Task<ApiResponse<EmployeePortalProfileDto>> GetProfileAsync(CancellationToken cancellationToken = default);
}
