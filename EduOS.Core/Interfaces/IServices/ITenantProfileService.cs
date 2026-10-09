using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant's canonical profile and platform-owned subdomain; private-domain hosts are managed through TenantDomain.</summary>
public interface ITenantProfileService
{
    Task<ApiResponse<TenantDto>> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantDto>> UpdateProfileAsync(UpdateTenantProfileRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantDto>> UpdateRegionalSettingsAsync(UpdateTenantRegionalSettingsRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> UploadLogoAsync(PrivateFileUploadDto file, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> UploadFaviconAsync(PrivateFileUploadDto file, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> RemoveLogoAsync(string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> RemoveFaviconAsync(string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubdomainAvailabilityDto>> CheckSubdomainAvailabilityAsync(string subdomain, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantDto>> UpdateSubdomainAsync(UpdateTenantSubdomainRequestDto request, CancellationToken cancellationToken = default);
}
