using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

public interface IUsageHistoryService
{
    Task<ApiResponse<PagedResult<UsageStatisticsDto>>> GetHistoryAsync(
        int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<UsageStatisticsDto>> CaptureTodayAsync(CancellationToken ct = default);
}
