using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;

namespace EduOS.Core.Interfaces.IServices;

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
