using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace EduOS.Service.Services.Tenants
{
    public class TenantSettingService : ITenantSettingService
    {
        private readonly IGenericRepository<TenantSetting> _settingRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<TenantSettingService> _logger;
        private readonly IDataProtector _protector;

        // Setting categories
        private const string CATEGORY_SMS = "Sms";
        private const string CATEGORY_EMAIL = "Email";
        private const string PROTECTED_PREFIX = "dp:v1:";
        private const string SECRET_MASK = "********";

        private static readonly IReadOnlySet<string> SmsProviders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "BulkSMSBD", "SslWireless", "MimSms", "Twilio", "Custom"
            };

        private static readonly IReadOnlySet<int> SmtpPorts =
            new HashSet<int> { 25, 465, 587, 2525 };

        public TenantSettingService(
            IGenericRepository<TenantSetting> settingRepo,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<TenantSettingService> logger)
        {
            _settingRepo = settingRepo;
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _protector = dataProtectionProvider.CreateProtector(
                "EduOS.TenantSettings.SensitiveValues.v1");
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
                var provider = dto.Provider?.Trim();
                var apiUrl = dto.ApiUrl?.Trim();
                var senderId = dto.SenderId?.Trim();
                var validation = await ValidateSmsGatewayAsync(
                    provider, apiUrl, dto.ApiKey, senderId, dto.IsEnabled);
                if (validation != null) return validation;

                await UpsertSettingAsync(CATEGORY_SMS, "Provider", provider, false);
                await UpsertSettingAsync(CATEGORY_SMS, "ApiUrl", apiUrl, false);
                await UpsertSettingAsync(CATEGORY_SMS, "ApiKey", dto.ApiKey, true);
                await UpsertSettingAsync(CATEGORY_SMS, "SenderId", senderId, false);
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
                var smtpHost = dto.SmtpHost?.Trim();
                var smtpUsername = dto.SmtpUsername?.Trim();
                var fromEmail = dto.FromEmail?.Trim();
                var fromName = dto.FromName?.Trim();
                var validation = await ValidateEmailGatewayAsync(
                    smtpHost,
                    dto.SmtpPort,
                    smtpUsername,
                    dto.SmtpPassword,
                    fromEmail,
                    dto.IsEnabled);
                if (validation != null) return validation;

                await UpsertSettingAsync(CATEGORY_EMAIL, "SmtpHost", smtpHost, false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "SmtpPort", dto.SmtpPort?.ToString(), false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "SmtpUsername", smtpUsername, false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "SmtpPassword", dto.SmtpPassword, true);
                await UpsertSettingAsync(CATEGORY_EMAIL, "FromEmail", fromEmail, false);
                await UpsertSettingAsync(CATEGORY_EMAIL, "FromName", fromName, false);
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
                var value = setting?.IsSensitive == true
                    ? UnprotectSensitiveValue(setting.SettingValue)
                    : setting?.SettingValue;

                return ApiResponse<string?>.SuccessResponse(value);
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
                .ToDictionary(
                    s => s.SettingKey,
                    s => s.IsSensitive ? SECRET_MASK : s.SettingValue!)
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
                if (isSensitive && IsUnchangedSecret(value))
                    return;

                existing.SettingValue = isSensitive
                    ? ProtectSensitiveValue(value)
                    : value;
                existing.IsSensitive = isSensitive;
                _settingRepo.Update(existing);
            }
            else
            {
                if (isSensitive && IsUnchangedSecret(value))
                    return;

                var setting = new TenantSetting
                {
                    TenantId = _currentUser.TenantId,
                    Category = category,
                    SettingKey = key,
                    SettingValue = isSensitive
                        ? ProtectSensitiveValue(value)
                        : value,
                    IsSensitive = isSensitive,
                    IsEditable = true,
                    DataType = "string"
                };
                await _settingRepo.AddAsync(setting);
            }
        }

        private static bool IsUnchangedSecret(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                   || string.Equals(value, SECRET_MASK, StringComparison.Ordinal);
        }

        private async Task<ApiResponse<bool>?> ValidateSmsGatewayAsync(
            string? provider,
            string? apiUrl,
            string? apiKey,
            string? senderId,
            bool isEnabled)
        {
            if (!string.IsNullOrWhiteSpace(provider) && !SmsProviders.Contains(provider))
                return ApiResponse<bool>.ErrorResponse("Unsupported SMS provider", 400);

            if (!string.IsNullOrWhiteSpace(apiUrl) && !IsSafePublicHttpsUrl(apiUrl))
            {
                return ApiResponse<bool>.ErrorResponse(
                    "SMS API URL must be a public HTTPS address", 400);
            }

            if (!string.IsNullOrWhiteSpace(senderId)
                && !Regex.IsMatch(senderId, @"^[\p{L}\p{N}._ -]{1,20}$"))
            {
                return ApiResponse<bool>.ErrorResponse("Invalid SMS sender ID", 400);
            }

            if (!isEnabled) return null;

            if (string.IsNullOrWhiteSpace(provider)
                || string.IsNullOrWhiteSpace(apiUrl)
                || string.IsNullOrWhiteSpace(senderId))
            {
                return ApiResponse<bool>.ErrorResponse(
                    "Provider, public HTTPS API URL, and sender ID are required to enable SMS", 400);
            }

            if (IsUnchangedSecret(apiKey)
                && !await HasStoredSecretAsync(CATEGORY_SMS, "ApiKey"))
            {
                return ApiResponse<bool>.ErrorResponse(
                    "API key is required to enable SMS", 400);
            }

            return null;
        }

        private async Task<ApiResponse<bool>?> ValidateEmailGatewayAsync(
            string? smtpHost,
            int? smtpPort,
            string? smtpUsername,
            string? smtpPassword,
            string? fromEmail,
            bool isEnabled)
        {
            if (!string.IsNullOrWhiteSpace(smtpHost) && !IsSafePublicHost(smtpHost))
                return ApiResponse<bool>.ErrorResponse("SMTP host must be a public host", 400);

            if (smtpPort.HasValue && !SmtpPorts.Contains(smtpPort.Value))
            {
                return ApiResponse<bool>.ErrorResponse(
                    "SMTP port must be 25, 465, 587, or 2525", 400);
            }

            if (!string.IsNullOrWhiteSpace(fromEmail)
                && !new EmailAddressAttribute().IsValid(fromEmail))
            {
                return ApiResponse<bool>.ErrorResponse("Invalid sender email", 400);
            }

            if (!isEnabled) return null;

            if (string.IsNullOrWhiteSpace(smtpHost)
                || !smtpPort.HasValue
                || string.IsNullOrWhiteSpace(fromEmail))
            {
                return ApiResponse<bool>.ErrorResponse(
                    "SMTP host, approved port, and sender email are required to enable email", 400);
            }

            if (!string.IsNullOrWhiteSpace(smtpUsername)
                && IsUnchangedSecret(smtpPassword)
                && !await HasStoredSecretAsync(CATEGORY_EMAIL, "SmtpPassword"))
            {
                return ApiResponse<bool>.ErrorResponse(
                    "SMTP password is required when a username is used", 400);
            }

            return null;
        }

        private async Task<bool> HasStoredSecretAsync(string category, string key)
        {
            var existing = (await _settingRepo.FindAsync(s =>
                s.TenantId == _currentUser.TenantId
                && s.Category == category
                && s.SettingKey == key)).FirstOrDefault();
            return existing?.IsSensitive == true
                   && !string.IsNullOrWhiteSpace(existing.SettingValue);
        }

        private static bool IsSafePublicHttpsUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                   && uri.Scheme == Uri.UriSchemeHttps
                   && string.IsNullOrEmpty(uri.UserInfo)
                   && IsSafePublicHost(uri.Host);
        }

        private static bool IsSafePublicHost(string host)
        {
            var normalized = host.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".test", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IPAddress.TryParse(normalized, out var address))
                return !IsPrivateAddress(address);

            return normalized.Contains('.')
                   && Uri.CheckHostName(normalized) == UriHostNameType.Dns;
        }

        private static bool IsPrivateAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address)) return true;
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv4MappedToIPv6)
                    return IsPrivateAddress(address.MapToIPv4());

                return address.IsIPv6LinkLocal
                       || address.IsIPv6SiteLocal
                       || address.IsIPv6Multicast
                       || address.Equals(IPAddress.IPv6Any)
                       || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
            }

            var bytes = address.GetAddressBytes();
            return bytes[0] == 0
                   || bytes[0] == 10
                   || bytes[0] == 127
                   || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                   || (bytes[0] == 169 && bytes[1] == 254)
                   || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 198 && bytes[1] is 18 or 19)
                   || bytes[0] >= 224;
        }

        private string? ProtectSensitiveValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return PROTECTED_PREFIX + _protector.Protect(value);
        }

        private string? UnprotectSensitiveValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            // Legacy rows may still be plaintext. They are never returned by the
            // category/gateway APIs and will be protected on their next update.
            if (!value.StartsWith(PROTECTED_PREFIX, StringComparison.Ordinal))
                return value;

            try
            {
                return _protector.Unprotect(value[PROTECTED_PREFIX.Length..]);
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning(ex,
                    "Unable to decrypt a sensitive tenant setting for tenant {TenantId}",
                    _currentUser.TenantId);
                return null;
            }
        }
    }
}
