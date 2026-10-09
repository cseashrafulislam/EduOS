using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

public interface ITenantProfileService
    {
        Task<ApiResponse<TenantProfileDto>> GetProfileAsync();
        Task<ApiResponse<bool>> UpdateProfileAsync(UpdateTenantProfileDto dto);

        // Branding
        Task<ApiResponse<bool>> UpdateBrandingAsync(UpdateBrandingDto dto);
        Task<ApiResponse<string>> UploadLogoAsync(PrivateFileUploadDto file);
        Task<ApiResponse<string>> UploadFaviconAsync(PrivateFileUploadDto file);
        Task<ApiResponse<bool>> RemoveLogoAsync();
        Task<ApiResponse<bool>> RemoveFaviconAsync();

        // Subdomain
        Task<ApiResponse<SubdomainCheckResult>> CheckSubdomainAvailabilityAsync(string subdomain);
        Task<ApiResponse<bool>> UpdateSubdomainAsync(UpdateSubdomainDto dto);

        // General settings
        Task<ApiResponse<bool>> UpdateGeneralSettingsAsync(UpdateGeneralSettingsDto dto);
    }
