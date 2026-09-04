using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants
{
    public class TenantSettingService : ITenantSettingService
    {
        private readonly IGenericRepository<TenantSetting> _settingRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<TenantSettingService> _logger;

        // Setting categories
        private const string CATEGORY_SMS = "Sms";
        private const string CATEGORY_EMAIL = "Email";

        public TenantSettingService(
            IGenericRepository<TenantSetting> settingRepo,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            ILogger<TenantSettingService> logger)
        {
            _settingRepo = settingRepo;
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _logger = logger;
        }

        // ============================================================
        // SMS GATEWAY
        // ============================================================
        public async Task<ApiResponse<SmsGatewaySettingsDto>> GetSmsGatewayAsync()
        {
            try
            {
                var settings = await GetCategoryDictAsync(CATEGORY_SMS);

                var dto = new SmsGatewaySettingsDto
                {
                    Provider = settings.GetValueOrDefault("Provider"),
                    ApiUrl = settings.GetValueOrDefault("ApiUrl"),
                    ApiKey = settings.GetValueOrDefault("ApiKey"),
                    SenderId = settings.GetValueOrDefault("SenderId"),
                    IsEnabled = bool.TryParse(settings.GetValueOrDefault("IsEnabled"), out var en) && en
                };

                return ApiResponse<SmsGatewaySettingsDto>.SuccessResponse(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load SMS gateway settings");
                return ApiResponse<SmsGatewaySettingsDto>.ErrorResponse("Failed to load settings", 500);
            }
        }

        public async Task<ApiResponse<bool>> SaveSmsGatewayAsync(SmsGatewaySettingsDto dto)
        {
            try
            {
                await UpsertSettingAsync(CATEGORY_SMS, "Provider", dto.Provider, false);
                await UpsertSettingAsync(CATEGORY_SMS, "ApiUrl", dto.ApiUrl, false);
                await UpsertSettingAsync(CATEGORY_SMS, "ApiKey", dto.ApiKey, true);
                await UpsertSettingAsync(CATEGORY_SMS, "SenderId", dto.SenderId, false);
                await UpsertSettingAsync(CATEGORY_SMS, "IsEnabled", dto.IsEnabled.ToString(), false);

                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("SMS gateway settings saved for tenant {Id}", _currentUser.TenantId);
                return ApiResponse<bool>.SuccessResponse(true, "SMS gateway settings saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save SMS gateway settings");
                return ApiResponse<bool>.ErrorResponse("Failed to save settings", 500);
            }
        }

        // ============================================================
        // EMAIL GATEWAY
        // ============================================================
        public async Task<ApiResponse<EmailGatewaySettingsDto>> GetEmailGatewayAsync()
        {
            try
            {
                var settings = await GetCategoryDictAsync(CATEGORY_EMAIL);

                var dto = new EmailGatewaySettingsDto
                {
                    SmtpHost = settings.GetValueOrDefault("SmtpHost"),
                    SmtpPort = int.TryParse(settings.GetValueOrDefault("SmtpPort"), out var port) ? port : null,
                    SmtpUsername = settings.GetValueOrDefault("SmtpUsername"),
                    SmtpPassword = settings.GetValueOrDefault("SmtpPassword"),
                    FromEmail = settings.GetValueOrDefault("FromEmail"),
                    FromName = settings.GetValueOrDefault("FromName"),
                    UseSsl = bool.TryParse(settings.GetValueOrDefault("UseSsl"), out var ssl) && ssl,
                    IsEnabled = bool.TryParse(settings.GetValueOrDefault("IsEnabled"), out var en) && en
                };

                return ApiResponse<EmailGatewaySettingsDto>.SuccessResponse(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load email gateway settings");
                return ApiResponse<EmailGatewaySettingsDto>.ErrorResponse("Failed to load settings", 500);
            }
        }

        public async Task<ApiResponse<bool>> SaveEmailGatewayAsync(EmailGatewaySettingsDto dto)
        {
            try
            {
                await UpsertSettingAsync(CATEGORY_EMAIL, "SmtpHost", dto.SmtpHost, false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "SmtpPort", dto.SmtpPort?.ToString(), false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "SmtpUsername", dto.SmtpUsername, false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "SmtpPassword", dto.SmtpPassword, true);
                await UpsertSettingAsync(CATEGORY_EMAIL, "FromEmail", dto.FromEmail, false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "FromName", dto.FromName, false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "UseSsl", dto.UseSsl.ToString(), false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "IsEnabled", dto.IsEnabled.ToString(), false);

                await _unitOfWork.SaveChangesAsync();

                return ApiResponse<bool>.SuccessResponse(true, "Email gateway settings saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save email gateway settings");
                return ApiResponse<bool>.ErrorResponse("Failed to save settings", 500);
            }
        }

        // ============================================================
        // GENERIC KEY-VALUE
        // ============================================================
        public async Task<ApiResponse<string?>> GetSettingAsync(string category, string key)
        {
            try
            {
                var settings = await _settingRepo.FindAsync(s =>
                    s.TenantId == _currentUser.TenantId &&
                    s.Category == category &&
                    s.SettingKey == key);

                var setting = settings?.FirstOrDefault();
                return ApiResponse<string?>.SuccessResponse(setting?.SettingValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get setting {Category}.{Key}", category, key);
                return ApiResponse<string?>.ErrorResponse("Failed to get setting", 500);
            }
        }

        public async Task<ApiResponse<bool>> SaveSettingAsync(string category, string key, string value, bool isSensitive = false)
        {
            try
            {
                await UpsertSettingAsync(category, key, value, isSensitive);
                await _unitOfWork.SaveChangesAsync();
                return ApiResponse<bool>.SuccessResponse(true, "Setting saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save setting");
                return ApiResponse<bool>.ErrorResponse("Failed to save setting", 500);
            }
        }

        public async Task<ApiResponse<Dictionary<string, string>>> GetAllByCategoryAsync(string category)
        {
            try
            {
                var dict = await GetCategoryDictAsync(category);
                return ApiResponse<Dictionary<string, string>>.SuccessResponse(dict);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load category {Category}", category);
                return ApiResponse<Dictionary<string, string>>.ErrorResponse("Failed", 500);
            }
        }

        // ============================================================
        // INTERNAL HELPERS
        // ============================================================
        private async Task<Dictionary<string, string>> GetCategoryDictAsync(string category)
        {
            var settings = await _settingRepo.FindAsync(s =>
                s.TenantId == _currentUser.TenantId &&
                s.Category == category);

            return settings?
                .Where(s => s.SettingValue != null)
                .ToDictionary(s => s.SettingKey, s => s.SettingValue!)
                ?? new Dictionary<string, string>();
        }

        private async Task UpsertSettingAsync(string category, string key, string? value, bool isSensitive)
        {
            var existing = (await _settingRepo.FindAsync(s =>
                s.TenantId == _currentUser.TenantId &&
                s.Category == category &&
                s.SettingKey == key))?.FirstOrDefault();

            if (existing != null)
            {
                existing.SettingValue = value;
                existing.IsSensitive = isSensitive;
                _settingRepo.Update(existing);
            }
            else
            {
                var setting = new TenantSetting
                {
                    TenantId = _currentUser.TenantId,
                    Category = category,
                    SettingKey = key,
                    SettingValue = value,
                    IsSensitive = isSensitive,
                    IsEditable = true,
                    DataType = "string"
                };
                await _settingRepo.AddAsync(setting);
            }
        }
    }
}
