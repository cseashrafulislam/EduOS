using EduOS.Core.Common;
using EduOS.Core.DTOs.Dashboard;

namespace EduOS.Core.Interfaces.IServices
{
    public interface IDashboardService
    {
        Task<ApiResponse<DashboardVm>> GetDashboardAsync();
    }
}
