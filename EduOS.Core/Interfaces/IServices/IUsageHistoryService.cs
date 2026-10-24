using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

public interface IUsageHistoryService
{
    Task<ApiResponse<PagedResult<UsageStatisticsDto>>> GetHistoryAsync(
        int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<string>> ExportHistoryCsvAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);
    Task<ApiResponse<UsageStatisticsDto>> CaptureTodayAsync(CancellationToken ct = default);
    Task<ApiResponse<UsageCaptureAutomationDto>> GetAutomationStatusAsync(CancellationToken ct = default);
    Task<ApiResponse<UsageQuotaOverviewDto>> GetQuotaOverviewAsync(CancellationToken ct = default);
}
