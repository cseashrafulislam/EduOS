using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
namespace EduOS.Core.Interfaces.IServices;
public interface IApiCredentialUsageService
{
    Task<ApiResponse<PagedResult<UsageStatisticsDto>>> GetUsageAsync(string? authorizationHeader, int page, int pageSize, CancellationToken ct = default);
}
