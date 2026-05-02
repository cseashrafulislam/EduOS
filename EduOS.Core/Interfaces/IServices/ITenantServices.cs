using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using Microsoft.AspNetCore.Http;

namespace EduOS.Core.Interfaces.IServices
{
    public interface ITenantProfileService
    {
        Task<ApiResponse<TenantProfileDto>> GetProfileAsync();
        Task<ApiResponse<bool>> UpdateProfileAsync(UpdateTenantProfileDto dto);

        // Branding
        Task<ApiResponse<bool>> UpdateBrandingAsync(UpdateBrandingDto dto);
        Task<ApiResponse<string>> UploadLogoAsync(IFormFile file);
        Task<ApiResponse<string>> UploadFaviconAsync(IFormFile file);
        Task<ApiResponse<bool>> RemoveLogoAsync();
        Task<ApiResponse<bool>> RemoveFaviconAsync();

        // Subdomain
        Task<ApiResponse<SubdomainCheckResult>> CheckSubdomainAvailabilityAsync(string subdomain);
        Task<ApiResponse<bool>> UpdateSubdomainAsync(UpdateSubdomainDto dto);

        // General settings
        Task<ApiResponse<bool>> UpdateGeneralSettingsAsync(UpdateGeneralSettingsDto dto);
    }

    public interface ITenantSettingService
    {
        // SMS Gateway
        Task<ApiResponse<SmsGatewaySettingsDto>> GetSmsGatewayAsync();
        Task<ApiResponse<bool>> SaveSmsGatewayAsync(SmsGatewaySettingsDto dto);

        // Email Gateway
        Task<ApiResponse<EmailGatewaySettingsDto>> GetEmailGatewayAsync();
        Task<ApiResponse<bool>> SaveEmailGatewayAsync(EmailGatewaySettingsDto dto);

        // Generic key-value operations
        Task<ApiResponse<string?>> GetSettingAsync(string category, string key);
        Task<ApiResponse<bool>> SaveSettingAsync(string category, string key, string value, bool isSensitive = false);
        Task<ApiResponse<Dictionary<string, string>>> GetAllByCategoryAsync(string category);
    }

    public interface IOnboardingService
    {
        Task<ApiResponse<OnboardingStatusDto>> GetStatusAsync();
        Task<ApiResponse<bool>> AdvanceToStepAsync(EduOS.Core.Enums.OnboardingStep step);
        Task<ApiResponse<bool>> CompleteStepAsync(CompleteStepDto dto);
        Task<ApiResponse<bool>> CompleteOnboardingAsync();
    }
}
