using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IServices;

public interface IPlatformTenantLifecycleService
{
    Task<ApiResponse<PagedResult<PlatformTenantLifecycleDto>>> SearchAsync(
        string? search, TenantState? state, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PlatformTenantLifecycleDto>> ChangeStateAsync(
        long tenantId, ChangePlatformTenantStateRequestDto request, CancellationToken ct = default);
}
