using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
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

namespace EduOS.Service.Services.Tenants
{
    public class TenantProfileService : ITenantProfileService
    {
        private readonly IGenericRepository<Tenant> _tenantRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IFileUploadService _fileStorage;
        private readonly FileUploadSettings _fileSettings;
        private readonly string _portalBaseDomain;
        private readonly ILogger<TenantProfileService> _logger;

        private static readonly IReadOnlySet<string> SupportedCurrencies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "BDT", "USD", "INR", "GBP", "EUR", "AUD", "CAD", "SGD", "MYR", "AED"
            };

        private static readonly IReadOnlySet<string> SupportedTimeZones =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Asia/Dhaka", "Asia/Kolkata", "Asia/Karachi", "Asia/Dubai",
                "UTC", "America/New_York", "Europe/London"
            };

        private static readonly IReadOnlySet<string> SupportedLanguages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en", "bn-BD" };

        private static readonly IReadOnlySet<string> SupportedDateFormats =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "dd-MM-yyyy", "MM-dd-yyyy", "yyyy-MM-dd", "dd/MM/yyyy"
            };

        // Reserved subdomains that cannot be used
        private static readonly HashSet<string> _reservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "www", "api", "admin", "app", "mail", "ftp", "test", "staging", "dev",
            "demo", "blog", "shop", "store", "support", "help", "docs", "status",
            "eduos", "dashboard", "portal", "login", "signup", "billing", "secure"
        };

        public TenantProfileService(
            IGenericRepository<Tenant> tenantRepo,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IFileUploadService fileStorage,
            IOptions<FileUploadSettings> fileSettings,
            IOptions<TenantPortalSettings> portalSettings,
            ILogger<TenantProfileService> logger)
        {
            _tenantRepo = tenantRepo;
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _fileStorage = fileStorage;
            _fileSettings = fileSettings.Value;
            _portalBaseDomain = NormalizeBaseDomain(portalSettings.Value.BaseDomain);
            _logger = logger;
        }

        // ============================================================
        // GET PROFILE
        // ============================================================
        public async Task<ApiResponse<TenantProfileDto>> GetProfileAsync()
        {
            try
            {
                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<TenantProfileDto>.ErrorResponse("Tenant not found", 404);

                var dto = MapToProfileDto(tenant);
                return ApiResponse<TenantProfileDto>.SuccessResponse(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tenant profile");
                return ApiResponse<TenantProfileDto>.ErrorResponse("Failed to load profile", 500);
            }
        }

        // ============================================================
        // UPDATE PROFILE
        // ============================================================
        public async Task<ApiResponse<bool>> UpdateProfileAsync(UpdateTenantProfileDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return ApiResponse<bool>.ErrorResponse("Institution name is required", 400);

                if (string.IsNullOrWhiteSpace(dto.OwnerName))
                    return ApiResponse<bool>.ErrorResponse("Owner name is required", 400);

                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                tenant.Name = dto.Name.Trim();
                tenant.InstitutionType = dto.InstitutionType?.Trim();
                tenant.Phone = dto.Phone?.Trim();
                tenant.Website = dto.Website?.Trim();
                tenant.Address = dto.Address?.Trim();
                tenant.City = dto.City?.Trim();
                tenant.State = dto.State?.Trim();
                tenant.Country = dto.Country?.Trim();
                tenant.PostalCode = dto.PostalCode?.Trim();

                tenant.OwnerName = dto.OwnerName.Trim();
                tenant.OwnerPhone = dto.OwnerPhone?.Trim();
                tenant.OwnerEmail = dto.OwnerEmail?.Trim();
                tenant.OwnerDesignation = dto.OwnerDesignation?.Trim();

                _tenantRepo.Update(tenant);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Tenant {Id} profile updated", tenant.Id);
                return ApiResponse<bool>.SuccessResponse(true, "Profile updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update profile");
                return ApiResponse<bool>.ErrorResponse("Failed to update profile", 500);
            }
        }

        // ============================================================
        // BRANDING - COLORS
        // ============================================================
        public async Task<ApiResponse<bool>> UpdateBrandingAsync(UpdateBrandingDto dto)
        {
            try
            {
                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                if (!string.IsNullOrEmpty(dto.PrimaryColor) && !IsValidHexColor(dto.PrimaryColor))
                    return ApiResponse<bool>.ErrorResponse("Invalid primary color format. Use #RRGGBB", 400);

                if (!string.IsNullOrEmpty(dto.SecondaryColor) && !IsValidHexColor(dto.SecondaryColor))
                    return ApiResponse<bool>.ErrorResponse("Invalid secondary color format. Use #RRGGBB", 400);

                if (!string.IsNullOrEmpty(dto.AccentColor) && !IsValidHexColor(dto.AccentColor))
                    return ApiResponse<bool>.ErrorResponse("Invalid accent color format. Use #RRGGBB", 400);

                tenant.PrimaryColor = dto.PrimaryColor?.Trim() ?? tenant.PrimaryColor;
                tenant.SecondaryColor = dto.SecondaryColor?.Trim() ?? tenant.SecondaryColor;
                tenant.AccentColor = dto.AccentColor?.Trim() ?? tenant.AccentColor;

                _tenantRepo.Update(tenant);
                await _unitOfWork.SaveChangesAsync();

                return ApiResponse<bool>.SuccessResponse(true, "Branding updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update branding");
                return ApiResponse<bool>.ErrorResponse("Failed to update branding", 500);
            }
        }

        // ============================================================
        // UPLOAD LOGO
        // ============================================================
        public async Task<ApiResponse<string>> UploadLogoAsync(IFormFile file)
        {
            return await UploadBrandAssetAsync(file, isFavicon: false);
        }

        // ============================================================
        // UPLOAD FAVICON
        // ============================================================
        public async Task<ApiResponse<string>> UploadFaviconAsync(IFormFile file)
        {
            return await UploadBrandAssetAsync(file, isFavicon: true);
        }

        // ============================================================
        // REMOVE LOGO / FAVICON
        // ============================================================
        public async Task<ApiResponse<bool>> RemoveLogoAsync()
        {
            try
            {
                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                if (!string.IsNullOrEmpty(tenant.LogoUrl))
                {
                    var previousUrl = tenant.LogoUrl;
                    tenant.LogoUrl = null;
                    _tenantRepo.Update(tenant);
                    await _unitOfWork.SaveChangesAsync();
                    await DeleteReplacedAssetAsync(previousUrl, tenant.Id);
                }

                return ApiResponse<bool>.SuccessResponse(true, "Logo removed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove logo");
                return ApiResponse<bool>.ErrorResponse("Failed to remove logo", 500);
            }
        }

        public async Task<ApiResponse<bool>> RemoveFaviconAsync()
        {
            try
            {
                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                if (!string.IsNullOrEmpty(tenant.FaviconUrl))
                {
                    var previousUrl = tenant.FaviconUrl;
                    tenant.FaviconUrl = null;
                    _tenantRepo.Update(tenant);
                    await _unitOfWork.SaveChangesAsync();
                    await DeleteReplacedAssetAsync(previousUrl, tenant.Id);
                }

                return ApiResponse<bool>.SuccessResponse(true, "Favicon removed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove favicon");
                return ApiResponse<bool>.ErrorResponse("Failed to remove favicon", 500);
            }
        }

        // ============================================================
        // SUBDOMAIN CHECK
        // ============================================================
        public async Task<ApiResponse<SubdomainCheckResult>> CheckSubdomainAvailabilityAsync(string subdomain)
        {
            try
            {
                var result = new SubdomainCheckResult
                {
                    Subdomain = subdomain?.Trim().ToLowerInvariant()
                };

                // Validation
                if (string.IsNullOrWhiteSpace(result.Subdomain))
                {
                    result.Message = "Subdomain is required";
                    return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
                }

                if (result.Subdomain.Length < 3)
                {
                    result.Message = "Minimum 3 characters required";
                    return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
                }

                if (result.Subdomain.Length > 50)
                {
                    result.Message = "Maximum 50 characters allowed";
                    return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
                }

                // Pattern: lowercase letters, digits, hyphens. Cannot start/end with hyphen.
                if (!Regex.IsMatch(result.Subdomain, @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$"))
                {
                    result.Message = "Use only lowercase letters, numbers, and hyphens";
                    return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
                }

                if (_reservedSubdomains.Contains(result.Subdomain))
                {
                    result.Message = "This subdomain is reserved";
                    return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
                }

                result.IsValid = true;

                // Check uniqueness in DB
                var existing = await _tenantRepo
                    .FindAsync(t => t.Subdomain == result.Subdomain && t.Id != _currentUser.TenantId);

                if (existing != null && existing.Any())
                {
                    result.Message = "This subdomain is already taken";
                    result.IsAvailable = false;
                }
                else
                {
                    result.IsAvailable = true;
                    result.Message = "Available!";
                    result.FullUrl = $"https://{result.Subdomain}.{_portalBaseDomain}";
                }

                return ApiResponse<SubdomainCheckResult>.SuccessResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subdomain check failed");
                return ApiResponse<SubdomainCheckResult>.ErrorResponse("Check failed", 500);
            }
        }

        // ============================================================
        // UPDATE SUBDOMAIN
        // ============================================================
        public async Task<ApiResponse<bool>> UpdateSubdomainAsync(UpdateSubdomainDto dto)
        {
            try
            {
                var check = await CheckSubdomainAvailabilityAsync(dto.Subdomain);
                if (!check.Success || check.Data == null || !check.Data.IsAvailable)
                    return ApiResponse<bool>.ErrorResponse(
                        check.Data?.Message ?? "Subdomain unavailable", 400);

                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                tenant.Subdomain = dto.Subdomain.Trim().ToLowerInvariant();
                _tenantRepo.Update(tenant);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Subdomain set to '{Subdomain}' for tenant {Id}",
                    tenant.Subdomain, tenant.Id);

                return ApiResponse<bool>.SuccessResponse(true, "Subdomain saved successfully");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex,
                    "Subdomain update conflicted for tenant {TenantId}", _currentUser.TenantId);
                return ApiResponse<bool>.ErrorResponse(
                    "This subdomain was just taken. Choose another one.", 409);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update subdomain");
                return ApiResponse<bool>.ErrorResponse("Failed to update subdomain", 500);
            }
        }

        // ============================================================
        // UPDATE GENERAL SETTINGS
        // ============================================================
        public async Task<ApiResponse<bool>> UpdateGeneralSettingsAsync(UpdateGeneralSettingsDto dto)
        {
            try
            {
                var currency = dto.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
                var currencySymbol = dto.CurrencySymbol?.Trim() ?? string.Empty;
                var timeZone = dto.TimeZone?.Trim() ?? string.Empty;
                var language = NormalizeLanguage(dto.Language);
                var dateFormat = dto.DateFormat?.Trim() ?? string.Empty;

                if (!SupportedCurrencies.Contains(currency))
                    return ApiResponse<bool>.ErrorResponse("Unsupported currency", 400);
                if (string.IsNullOrWhiteSpace(currencySymbol) || currencySymbol.Length > 10)
                    return ApiResponse<bool>.ErrorResponse("Invalid currency symbol", 400);
                if (!SupportedTimeZones.Contains(timeZone))
                    return ApiResponse<bool>.ErrorResponse("Unsupported time zone", 400);
                if (!SupportedLanguages.Contains(language))
                    return ApiResponse<bool>.ErrorResponse("Unsupported language", 400);
                if (!SupportedDateFormats.Contains(dateFormat))
                    return ApiResponse<bool>.ErrorResponse("Unsupported date format", 400);

                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                tenant.Currency = currency;
                tenant.CurrencySymbol = currencySymbol;
                tenant.TimeZone = timeZone;
                tenant.Language = language;
                tenant.DateFormat = dateFormat;

                _tenantRepo.Update(tenant);
                await _unitOfWork.SaveChangesAsync();

                return ApiResponse<bool>.SuccessResponse(true, "Settings saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update general settings");
                return ApiResponse<bool>.ErrorResponse("Failed to update settings", 500);
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================
        private static bool IsValidHexColor(string color)
        {
            return Regex.IsMatch(color, @"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$");
        }

        private async Task<ApiResponse<string>> UploadBrandAssetAsync(
            IFormFile file,
            bool isFavicon)
        {
            var assetName = isFavicon ? "Favicon" : "Logo";
            string? uploadedUrl = null;

            try
            {
                if (file == null || file.Length == 0)
                    return ApiResponse<string>.ErrorResponse("No file provided", 400);

                var maxBytes = Math.Max(1, _fileSettings.MaxFileSizeMb) * 1024L * 1024L;
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var allowedExtensions = isFavicon
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" }
                    : new HashSet<string>(
                        _fileSettings.AllowedImageExtensions,
                        StringComparer.OrdinalIgnoreCase);

                if (file.Length > maxBytes || !allowedExtensions.Contains(extension)
                    || !_fileStorage.ValidateFile(file))
                {
                    return ApiResponse<string>.ErrorResponse(
                        isFavicon
                            ? "Invalid favicon. Use PNG or JPG within the upload limit."
                            : "Invalid logo. Use JPG, PNG, or WEBP within the upload limit.",
                        400);
                }

                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<string>.ErrorResponse("Tenant not found", 404);

                var previousUrl = isFavicon ? tenant.FaviconUrl : tenant.LogoUrl;
                var upload = await _fileStorage.UploadAsync(
                    file,
                    $"tenants/{tenant.Id}/branding");
                if (!upload.Success || string.IsNullOrWhiteSpace(upload.FileUrl))
                {
                    return ApiResponse<string>.ErrorResponse(
                        upload.ErrorMessage ?? $"{assetName} upload failed", 400);
                }

                uploadedUrl = upload.FileUrl;
                if (isFavicon)
                    tenant.FaviconUrl = uploadedUrl;
                else
                    tenant.LogoUrl = uploadedUrl;

                _tenantRepo.Update(tenant);
                try
                {
                    await _unitOfWork.SaveChangesAsync();
                }
                catch
                {
                    await _fileStorage.DeleteAsync(uploadedUrl);
                    throw;
                }

                if (!string.IsNullOrWhiteSpace(previousUrl)
                    && !string.Equals(previousUrl, uploadedUrl, StringComparison.Ordinal))
                {
                    await DeleteReplacedAssetAsync(previousUrl, tenant.Id);
                }

                _logger.LogInformation(
                    "{AssetName} uploaded for tenant {TenantId}", assetName, tenant.Id);
                return ApiResponse<string>.SuccessResponse(
                    uploadedUrl,
                    $"{assetName} uploaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{AssetName} upload failed", assetName);
                return ApiResponse<string>.ErrorResponse($"{assetName} upload failed", 500);
            }
        }

        private async Task DeleteReplacedAssetAsync(string fileUrl, long tenantId)
        {
            if (!await _fileStorage.DeleteAsync(fileUrl))
            {
                _logger.LogWarning(
                    "Old branding asset could not be deleted for tenant {TenantId}", tenantId);
            }
        }

        private static string NormalizeLanguage(string? language)
        {
            var normalized = language?.Trim() ?? string.Empty;
            return string.Equals(normalized, "bn", StringComparison.OrdinalIgnoreCase)
                ? "bn-BD"
                : normalized;
        }

        private static string NormalizeBaseDomain(string? baseDomain)
        {
            var normalized = (baseDomain ?? string.Empty)
                .Trim()
                .Trim('.')
                .ToLowerInvariant();
            return Regex.IsMatch(
                normalized,
                @"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")
                ? normalized
                : "eduos.com";
        }

        private static TenantProfileDto MapToProfileDto(Tenant t)
        {
            return new TenantProfileDto
            {
                Id = t.Id,
                Name = t.Name,
                Code = t.Code,
                Subdomain = t.Subdomain,
                CustomDomain = t.CustomDomain,
                InstitutionType = t.InstitutionType,
                Email = t.Email,
                Phone = t.Phone,
                Website = t.Website,
                Address = t.Address,
                City = t.City,
                State = t.State,
                Country = t.Country,
                PostalCode = t.PostalCode,
                OwnerName = t.OwnerName,
                OwnerPhone = t.OwnerPhone,
                OwnerEmail = t.OwnerEmail,
                OwnerDesignation = t.OwnerDesignation,
                LogoUrl = t.LogoUrl,
                FaviconUrl = t.FaviconUrl,
                PrimaryColor = t.PrimaryColor,
                SecondaryColor = t.SecondaryColor,
                AccentColor = t.AccentColor,
                Currency = t.Currency,
                CurrencySymbol = t.CurrencySymbol,
                TimeZone = t.TimeZone,
                Language = t.Language,
                DateFormat = t.DateFormat,
                IsEmailVerified = t.IsEmailVerified,
                IsOnboardingComplete = t.IsOnboardingComplete,
                OnboardingStep = (int)t.OnboardingStep,
                Status = t.Status.ToString()
            };
        }
    }
}
