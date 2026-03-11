using EduOS.Core.DTOs.Dashboard;

namespace EduOS.Core.Interfaces.SaaS
{
    public interface IDashboardService
    {
        Task<DashboardVm?> GetDashboardAsync();
    }
}