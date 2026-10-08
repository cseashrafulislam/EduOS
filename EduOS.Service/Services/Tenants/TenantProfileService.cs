using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Service.Helpers.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace EduOS.Service.Services.Tenants;

public sealed class TenantProfileService : ITenantProfileService
{
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<TenantSetting> _settings;
    private readonly IGenericRepository<InstitutionTypeDefinition> _institutionTypes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IFileUploadService _fileStorage;
    private readonly FileUploadSettings _fileSettings;
    private readonly string _baseDomain;
    private readonly ILogger<TenantProfileService> _logger;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "app", "mail", "ftp", "test", "staging", "dev", "demo",
        "blog", "shop", "store", "support", "help", "docs", "status", "eduos",
        "dashboard", "portal", "login", "signup", "billing", "secure"
    };
    private static readonly HashSet<string> Currencies = new(StringComparer.OrdinalIgnoreCase)
    { "BDT", "USD", "INR", "GBP", "EUR", "AUD", "CAD", "SGD", "MYR", "AED" };
    private static readonly HashSet<string> TimeZones = new(StringComparer.Ordinal)
    { "Asia/Dhaka", "Asia/Kolkata", "Asia/Karachi", "Asia/Dubai", "UTC", "America/New_York", "Europe/London" };
    private static readonly HashSet<string> DateFormats = new(StringComparer.Ordinal)
    { "dd-MM-yyyy", "MM-dd-yyyy", "yyyy-MM-dd", "dd/MM/yyyy" };

    public TenantProfileService(IGenericRepository<Tenant> tenants, IGenericRepository<TenantSetting> settings,
        IGenericRepository<InstitutionTypeDefinition> institutionTypes, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, IFileUploadService fileStorage,
        IOptions<FileUploadSettings> fileSettings, IOptions<TenantPortalSettings> portalSettings,
        ILogger<TenantProfileService> logger)
    {
        _tenants = tenants; _settings = settings; _institutionTypes = institutionTypes;
        _unitOfWork = unitOfWork; _currentUser = currentUser; _fileStorage = fileStorage;
        _fileSettings = fileSettings.Value;
        _baseDomain = NormalizeBaseDomain(portalSettings.Value.BaseDomain);
        _logger = logger;
    }

    public async Task<ApiResponse<TenantProfileDto>> GetProfileAsync()
    {
        if (!CanRead()) return ApiResponse<TenantProfileDto>.ErrorResponse("Tenant access is required.", 403);
        try
        {
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return ApiResponse<TenantProfileDto>.ErrorResponse("Institution not found.", 404);
            var extras = await ReadSettingsAsync(tenant.Id);
            var type = tenant.InstitutionTypeDefinitionId.HasValue
                ? await _institutionTypes.GetQueryable().AsNoTracking().Where(x => x.Id == tenant.InstitutionTypeDefinitionId.Value)
                    .Select(x => x.Name).FirstOrDefaultAsync() : null;
            var step = Enum.TryParse<OnboardingStep>(tenant.OnboardingStage.ToString(), out var legacyStep)
                ? (int)legacyStep : (int)OnboardingStep.EmailVerification;
            return ApiResponse<TenantProfileDto>.SuccessResponse(new TenantProfileDto
            {
                Id = tenant.Id, Name = tenant.Name, Code = tenant.Code,
                Subdomain = tenant.Subdomain, CustomDomain = tenant.CustomDomain,
                InstitutionType = type, Email = tenant.Email, Phone = tenant.Phone,
                Address = tenant.Address, Website = Get(extras, "Profile.Website"),
                City = Get(extras, "Profile.City"), State = Get(extras, "Profile.State"),
                Country = Get(extras, "Profile.Country") ?? tenant.CountryCode,
                PostalCode = Get(extras, "Profile.PostalCode"),
                OwnerName = Get(extras, "Profile.OwnerName") ?? string.Empty,
                OwnerPhone = Get(extras, "Profile.OwnerPhone"), OwnerEmail = Get(extras, "Profile.OwnerEmail"),
                OwnerDesignation = Get(extras, "Profile.OwnerDesignation"),
                LogoUrl = tenant.LogoUrl, FaviconUrl = tenant.FaviconUrl,
                PrimaryColor = Get(extras, "Branding.PrimaryColor"),
                SecondaryColor = Get(extras, "Branding.SecondaryColor"),
                AccentColor = Get(extras, "Branding.AccentColor"),
                Currency = tenant.CurrencyCode, CurrencySymbol = Get(extras, "General.CurrencySymbol"),
                TimeZone = tenant.TimeZoneId, Language = tenant.DefaultLanguage,
                DateFormat = Get(extras, "General.DateFormat"),
                IsEmailVerified = tenant.IsEmailVerified,
                IsOnboardingComplete = tenant.IsOnboardingComplete,
                OnboardingStep = step, Status = tenant.State.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant profile read failed for {TenantId}", _currentUser.TenantId);
            return ApiResponse<TenantProfileDto>.ErrorResponse("Institution profile could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<bool>> UpdateProfileAsync(UpdateTenantProfileDto request)
    {
        if (!CanManage()) return Denied();
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.OwnerName) || request.OwnerName.Trim().Length > 150)
            return ApiResponse<bool>.ErrorResponse("Institution and owner names are required.");
        try
        {
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return ApiResponse<bool>.ErrorResponse("Institution not found.", 404);
            if (!string.IsNullOrWhiteSpace(request.InstitutionType))
            {
                var input = request.InstitutionType.Trim();
                var type = await _institutionTypes.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IsActive && (x.Code == input || x.Name == input));
                if (type == null) return ApiResponse<bool>.ErrorResponse("Institution type is invalid.");
                tenant.InstitutionTypeDefinitionId = type.Id;
            }
            tenant.Name = request.Name.Trim();
            tenant.Phone = Trim(request.Phone);
            tenant.Address = Trim(request.Address);
            tenant.UpdatedAt = DateTime.UtcNow;
            tenant.UpdatedBy = _currentUser.UserId;
            await UpsertSettingsAsync(tenant.Id, "Profile", new Dictionary<string, string?>
            {
                ["Website"] = Trim(request.Website), ["City"] = Trim(request.City),
                ["State"] = Trim(request.State), ["Country"] = Trim(request.Country),
                ["PostalCode"] = Trim(request.PostalCode),
                ["OwnerName"] = request.OwnerName.Trim(),
                ["OwnerPhone"] = Trim(request.OwnerPhone),
                ["OwnerEmail"] = Trim(request.OwnerEmail),
                ["OwnerDesignation"] = Trim(request.OwnerDesignation)
            });
            await _unitOfWork.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Institution profile updated.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<bool>.ErrorResponse("Institution profile changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent profile update for {TenantId}", _currentUser.TenantId);
            return ApiResponse<bool>.ErrorResponse("Institution profile update conflicts with another request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Institution profile save failed for {TenantId}", _currentUser.TenantId);
            return ApiResponse<bool>.ErrorResponse("Institution profile could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<bool>> UpdateBrandingAsync(UpdateBrandingDto request)
    {
        if (!CanManage()) return Denied();
        if (request == null) return ApiResponse<bool>.ErrorResponse("Branding details are required.");
        foreach (var color in new[] { request.PrimaryColor, request.SecondaryColor, request.AccentColor })
            if (color != null && !Regex.IsMatch(color, "^#(?:[0-9a-fA-F]{6})$"))
                return ApiResponse<bool>.ErrorResponse("Brand colors must use #RRGGBB.");
        try
        {
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return ApiResponse<bool>.ErrorResponse("Institution not found.", 404);
            await UpsertSettingsAsync(tenant.Id, "Branding", new Dictionary<string, string?>
            {
                ["PrimaryColor"] = request.PrimaryColor, ["SecondaryColor"] = request.SecondaryColor,
                ["AccentColor"] = request.AccentColor
            }, ignoreNulls: true);
            await _unitOfWork.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Branding colors saved.");
        }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("Branding was changed. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Branding save failed for {TenantId}", _currentUser.TenantId);
            return ApiResponse<bool>.ErrorResponse("Branding could not be saved.", 500);
        }
    }

    public Task<ApiResponse<string>> UploadLogoAsync(IFormFile file) => UploadBrandAssetAsync(file, false);
    public Task<ApiResponse<string>> UploadFaviconAsync(IFormFile file) => UploadBrandAssetAsync(file, true);
    public Task<ApiResponse<bool>> RemoveLogoAsync() => RemoveBrandAssetAsync(false);
    public Task<ApiResponse<bool>> RemoveFaviconAsync() => RemoveBrandAssetAsync(true);

    public async Task<ApiResponse<SubdomainCheckResult>> CheckSubdomainAvailabilityAsync(string subdomain)
    {
        if (!CanRead()) return ApiResponse<SubdomainCheckResult>.ErrorResponse("Tenant access is required.", 403);
        var normalized = subdomain?.Trim().ToLowerInvariant() ?? string.Empty;
        var result = new SubdomainCheckResult { Subdomain = normalized };
        if (normalized.Length is < 3 or > 50 || !Regex.IsMatch(normalized, "^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$"))
        {
            result.Message = "Use 3–50 lowercase letters, numbers and hyphens.";
            return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
        }
        if (Reserved.Contains(normalized))
        {
            result.Message = "This subdomain is reserved.";
            return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
        }
        result.IsValid = true;
        var exists = await _tenants.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.Subdomain == normalized && x.Id != _currentUser.TenantId);
        result.IsAvailable = !exists;
        result.Message = exists ? "This subdomain is already registered." : "Available.";
        result.FullUrl = exists ? null : $"https://{normalized}.{_baseDomain}";
        return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
    }

    public async Task<ApiResponse<bool>> UpdateSubdomainAsync(UpdateSubdomainDto request)
    {
        if (!CanManage()) return Denied();
        if (request == null) return ApiResponse<bool>.ErrorResponse("Subdomain is required.");
        var check = await CheckSubdomainAvailabilityAsync(request.Subdomain);
        if (check.Data?.IsAvailable != true) return ApiResponse<bool>.ErrorResponse(check.Data?.Message ?? "Subdomain unavailable.");
        try
        {
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return ApiResponse<bool>.ErrorResponse("Institution not found.", 404);
            tenant.Subdomain = check.Data.Subdomain;
            tenant.UpdatedAt = DateTime.UtcNow;
            tenant.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Subdomain saved.");
        }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("Subdomain is already registered. Choose another one.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subdomain save failed for {TenantId}", _currentUser.TenantId);
            return ApiResponse<bool>.ErrorResponse("Subdomain could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<bool>> UpdateGeneralSettingsAsync(UpdateGeneralSettingsDto request)
    {
        if (!CanManage()) return Denied();
        if (request == null) return ApiResponse<bool>.ErrorResponse("General settings are required.");
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        var symbol = request.CurrencySymbol?.Trim() ?? string.Empty;
        var timezone = request.TimeZone?.Trim() ?? string.Empty;
        var language = request.Language?.Trim() ?? string.Empty;
        var dateFormat = request.DateFormat?.Trim() ?? string.Empty;
        if (!Currencies.Contains(currency) || symbol.Length is < 1 or > 10 ||
            !TimeZones.Contains(timezone) || !DateFormats.Contains(dateFormat) ||
            language is not ("en" or "en-BD" or "bn" or "bn-BD"))
            return ApiResponse<bool>.ErrorResponse("One or more general settings are invalid.");
        try
        {
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return ApiResponse<bool>.ErrorResponse("Institution not found.", 404);
            tenant.CurrencyCode = currency;
            tenant.TimeZoneId = timezone;
            tenant.DefaultLanguage = language is "bn" or "bn-BD" ? "bn-BD" : "en-BD";
            tenant.UpdatedAt = DateTime.UtcNow;
            tenant.UpdatedBy = _currentUser.UserId;
            await UpsertSettingsAsync(tenant.Id, "General", new Dictionary<string, string?>
            {
                ["CurrencySymbol"] = symbol, ["DateFormat"] = dateFormat
            });
            await _unitOfWork.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "General settings saved.");
        }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("General settings were changed. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "General settings failed for {TenantId}", _currentUser.TenantId);
            return ApiResponse<bool>.ErrorResponse("General settings could not be saved.", 500);
        }
    }

    private async Task<ApiResponse<string>> UploadBrandAssetAsync(IFormFile file, bool favicon)
    {
        if (!CanManage()) return ApiResponse<string>.ErrorResponse("Tenant administrator access is required.", 403);
        if (file == null || file.Length <= 0) return ApiResponse<string>.ErrorResponse("A valid image is required.");
        var allowed = favicon ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" } :
            new HashSet<string>(_fileSettings.AllowedImageExtensions, StringComparer.OrdinalIgnoreCase);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length > Math.Max(1, _fileSettings.MaxFileSizeMb) * 1024L * 1024L ||
            !allowed.Contains(extension) || !_fileStorage.ValidateFile(file))
            return ApiResponse<string>.ErrorResponse("Image format or file size is invalid.");
        try
        {
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return ApiResponse<string>.ErrorResponse("Institution not found.", 404);
            var upload = await _fileStorage.UploadAsync(file, $"tenants/{tenant.Id}/branding");
            if (!upload.Success || string.IsNullOrWhiteSpace(upload.FileUrl))
                return ApiResponse<string>.ErrorResponse(upload.ErrorMessage ?? "Image upload failed.");
            var old = favicon ? tenant.FaviconUrl : tenant.LogoUrl;
            if (favicon) tenant.FaviconUrl = upload.FileUrl;
            else tenant.LogoUrl = upload.FileUrl;
            tenant.UpdatedAt = DateTime.UtcNow;
            tenant.UpdatedBy = _currentUser.UserId;
            try { await _unitOfWork.SaveChangesAsync(); }
            catch
            {
                await _fileStorage.DeleteAsync(upload.FileUrl);
                throw;
            }
            if (old != null && old != upload.FileUrl) await DeleteReplacedAssetAsync(old, tenant.Id);
            return ApiResponse<string>.SuccessResponse(upload.FileUrl, "Brand image updated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<string>.ErrorResponse("Branding was modified. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Brand image upload failed for {TenantId}", _currentUser.TenantId);
            return ApiResponse<string>.ErrorResponse("Brand image could not be uploaded.", 500);
        }
    }

    private async Task<ApiResponse<bool>> RemoveBrandAssetAsync(bool favicon)
    {
        if (!CanManage()) return Denied();
        try
        {
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return ApiResponse<bool>.ErrorResponse("Institution not found.", 404);
            var old = favicon ? tenant.FaviconUrl : tenant.LogoUrl;
            if (old == null) return ApiResponse<bool>.SuccessResponse(true);
            if (favicon) tenant.FaviconUrl = null;
            else tenant.LogoUrl = null;
            tenant.UpdatedAt = DateTime.UtcNow;
            tenant.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync();
            await DeleteReplacedAssetAsync(old, tenant.Id);
            return ApiResponse<bool>.SuccessResponse(true, "Brand image removed.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Branding changed. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Brand image removal failed for {TenantId}", _currentUser.TenantId);
            return ApiResponse<bool>.ErrorResponse("Brand image could not be removed.", 500);
        }
    }

    private async Task DeleteReplacedAssetAsync(string url, long tenantId)
    {
        if (!await _fileStorage.DeleteAsync(url))
            _logger.LogWarning("Previous brand image could not be removed for tenant {TenantId}", tenantId);
    }

    private async Task<Tenant?> CurrentTenantAsync() =>
        await _tenants.GetQueryable().FirstOrDefaultAsync(x => x.Id == _currentUser.TenantId);

    private async Task<Dictionary<string, string>> ReadSettingsAsync(long tenantId) =>
        await _settings.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
            (x.Category == "Profile" || x.Category == "Branding" || x.Category == "General"))
            .ToDictionaryAsync(x => x.Key, x => x.Value);

    private async Task UpsertSettingsAsync(long tenantId, string category,
        IReadOnlyDictionary<string, string?> values, bool ignoreNulls = false)
    {
        var names = values.Keys.Select(x => category + "." + x).ToArray();
        var records = await _settings.GetQueryable().Where(x => x.TenantId == tenantId &&
            names.Contains(x.Key)).ToDictionaryAsync(x => x.Key);
        foreach (var value in values)
        {
            if (ignoreNulls && value.Value == null) continue;
            var key = category + "." + value.Key;
            var newValue = value.Value ?? string.Empty;
            if (records.TryGetValue(key, out var record))
            {
                if (record.Value == newValue) continue;
                record.Value = newValue;
                record.UpdatedAt = DateTime.UtcNow;
                record.UpdatedBy = _currentUser.UserId;
            }
            else
            {
                await _settings.AddAsync(new TenantSetting
                {
                    TenantId = tenantId, Key = key, Value = newValue, Category = category,
                    IsSensitive = false, CreatedAt = DateTime.UtcNow, CreatedBy = _currentUser.UserId
                });
            }
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string> records, string key) =>
        records.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeBaseDomain(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        return Regex.IsMatch(normalized, "^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$")
            ? normalized : "eduos.com";
    }
    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0;
    private bool CanManage() => CanRead() && _currentUser.IsTenantAdmin;
    private static ApiResponse<bool> Denied() => ApiResponse<bool>.ErrorResponse("Tenant administrator access is required.", 403);
}
