using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-scoped, deny-only feature restrictions. Never grants subscription features.</summary>
public interface ITenantFeatureRestrictionService
{
    Task<ApiResponse<PagedResult<TenantFeatureRestrictionDto>>> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantFeatureRestrictionDto>> SaveAsync(long featureId, SaveTenantFeatureRestrictionRequestDto request, CancellationToken cancellationToken = default);
}
