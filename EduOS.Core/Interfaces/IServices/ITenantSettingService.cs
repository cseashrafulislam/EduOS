using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant key/value settings. Secret material is never returned by value in read operations.</summary>
public interface ITenantSettingService
{
    Task<ApiResponse<PagedResult<TenantSettingDto>>> GetSettingsAsync(string? category, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantSettingDto>> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantSettingDto>> SaveSettingAsync(long? settingId, SaveTenantSettingRequestDto request, CancellationToken cancellationToken = default);
}
