using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
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
    private static readonly HashSet<string> AllowedDateFormats = new(StringComparer.Ordinal)
    {
        "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "MM-dd-yyyy"
    };
    private static readonly HashSet<string> AllowedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "en-BD", "bn-BD"
    };
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.Ordinal)
    {
        "BDT", "USD", "INR", "GBP", "EUR", "AED"
    };
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "app", "mail", "ftp", "test", "staging", "dev", "demo",
        "blog", "shop", "support", "docs", "status", "eduos", "portal", "login", "billing"
    };
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<TenantDomain> _domains;
    private readonly IGenericRepository<InstitutionTypeDefinition> _institutionTypes;
    private readonly IGenericRepository<AuditLog> _auditLogs;
    private readonly ICurrentUserService _user;
    private readonly IFileUploadService _storage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly FileUploadSettings _settings;
    private readonly ILogger<TenantProfileService> _logger;

    public TenantProfileService(IGenericRepository<Tenant> tenants, IGenericRepository<TenantDomain> domains,
        IGenericRepository<InstitutionTypeDefinition> institutionTypes, IGenericRepository<AuditLog> auditLogs,
        ICurrentUserService user,
        IFileUploadService storage, IUnitOfWork unitOfWork, IOptions<FileUploadSettings> options,
        ILogger<TenantProfileService> logger)
    {
        _tenants = tenants; _domains = domains; _institutionTypes = institutionTypes; _auditLogs = auditLogs;
        _user = user; _storage = storage; _unitOfWork = unitOfWork; _settings = options.Value; _logger = logger;
    }

    public async Task<ApiResponse<TenantDto>> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<TenantDto>.ErrorResponse("Tenant access is required.", 403);
        var tenant = await GetTenantAsync(cancellationToken);
        if (tenant == null) return ApiResponse<TenantDto>.ErrorResponse("Institution not found.", 404);
        return ApiResponse<TenantDto>.SuccessResponse(await MapAsync(tenant, cancellationToken));
    }

    public async Task<ApiResponse<TenantDto>> UpdateProfileAsync(UpdateTenantProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<TenantDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 ||
            request.LegalName?.Length > 250 || request.RegistrationNumber?.Length > 100 ||
            string.IsNullOrWhiteSpace(request.Email) || request.CountryCode == null || request.CountryCode.Length is < 2 or > 10)
            return ApiResponse<TenantDto>.ErrorResponse("Profile data is invalid.");
        try
        {
            var tenant = await GetTenantAsync(cancellationToken);
            if (tenant == null) return ApiResponse<TenantDto>.ErrorResponse("Institution not found.", 404);
            if (!MatchesVersion(tenant.RowVersion, request.RowVersion))
                return ApiResponse<TenantDto>.ErrorResponse("Profile changed. Reload and retry.", 409);
            if (!string.Equals(tenant.Email, request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                return ApiResponse<TenantDto>.ErrorResponse("Institution email changes require a verified email-change workflow.", 409);
            if (tenant.InstitutionTypeDefinitionId != request.InstitutionTypeDefinitionId)
                return ApiResponse<TenantDto>.ErrorResponse("Changing institution type requires a controlled onboarding workflow.", 409);
            var name = request.Name.Trim();
            var country = request.CountryCode.Trim().ToUpperInvariant();
            var phone = Trim(request.Phone);
            var address = Trim(request.Address);
            var changed = new List<string>();
            if (tenant.Name != name) changed.Add("Institution name");
            if (request.LegalName != null && tenant.LegalName != Trim(request.LegalName)) changed.Add("Legal name");
            if (request.RegistrationNumber != null && tenant.RegistrationNumber != Trim(request.RegistrationNumber)) changed.Add("Registration number");
            if (tenant.Phone != phone) changed.Add("Phone");
            if (tenant.Address != address) changed.Add("Address");
            if (tenant.CountryCode != country) changed.Add("Country");
            tenant.Name = name;
            if (request.LegalName != null) tenant.LegalName = Trim(request.LegalName);
            if (request.RegistrationNumber != null) tenant.RegistrationNumber = Trim(request.RegistrationNumber);
            tenant.Phone = phone; tenant.Address = address; tenant.CountryCode = country;
            tenant.UpdatedAt = DateTime.UtcNow; tenant.UpdatedBy = _user.UserId;
            if (changed.Count > 0) await RecordChangeAsync(tenant, "TenantProfileUpdated", changed);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<TenantDto>.SuccessResponse(await MapAsync(tenant, cancellationToken));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TenantDto>.ErrorResponse("Profile changed. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Tenant profile update failed for {TenantId}", _user.TenantId);
            return ApiResponse<TenantDto>.ErrorResponse("Profile could not be updated.", 500);
        }
    }

    public async Task<ApiResponse<TenantDto>> UpdateRegionalSettingsAsync(UpdateTenantRegionalSettingsRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<TenantDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request == null || !AllowedCurrencies.Contains(request.CurrencyCode ?? "") ||
            string.IsNullOrWhiteSpace(request.TimeZoneId) || !AllowedLanguages.Contains(request.DefaultLanguage ?? "") ||
            !AllowedDateFormats.Contains(request.DateFormat ?? ""))
            return ApiResponse<TenantDto>.ErrorResponse("Select valid currency, language and date format.");
        try
        {
            var tenant = await GetTenantAsync(cancellationToken);
            if (tenant == null) return ApiResponse<TenantDto>.ErrorResponse("Institution not found.", 404);
            if (!MatchesVersion(tenant.RowVersion, request.RowVersion))
                return ApiResponse<TenantDto>.ErrorResponse("Settings changed. Reload and retry.", 409);
            try { TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); }
            catch (TimeZoneNotFoundException) { return ApiResponse<TenantDto>.ErrorResponse("Time zone is not recognized."); }
            catch (InvalidTimeZoneException) { return ApiResponse<TenantDto>.ErrorResponse("Time zone is invalid."); }
            var zone = request.TimeZoneId!.Trim();
            var language = request.DefaultLanguage!.Trim();
            var changed = new List<string>();
            if (tenant.CurrencyCode != request.CurrencyCode) changed.Add("Currency");
            if (tenant.TimeZoneId != zone) changed.Add("Time zone");
            if (tenant.DefaultLanguage != language) changed.Add("Language");
            if (tenant.DateFormat != request.DateFormat) changed.Add("Date format");
            tenant.CurrencyCode = request.CurrencyCode!;
            tenant.TimeZoneId = zone; tenant.DefaultLanguage = language; tenant.DateFormat = request.DateFormat!;
            tenant.UpdatedAt = DateTime.UtcNow; tenant.UpdatedBy = _user.UserId;
            if (changed.Count > 0) await RecordChangeAsync(tenant, "TenantRegionalSettingsUpdated", changed);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<TenantDto>.SuccessResponse(await MapAsync(tenant, cancellationToken));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TenantDto>.ErrorResponse("Settings changed. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Regional settings update failed for {TenantId}", _user.TenantId);
            return ApiResponse<TenantDto>.ErrorResponse("Regional settings could not be updated.", 500);
        }
    }

    public Task<ApiResponse<string>> UploadLogoAsync(PrivateFileUploadDto file, CancellationToken cancellationToken = default) =>
        UploadBrandAssetAsync(file, false, cancellationToken);

    public Task<ApiResponse<string>> UploadFaviconAsync(PrivateFileUploadDto file, CancellationToken cancellationToken = default) =>
        UploadBrandAssetAsync(file, true, cancellationToken);

    public Task<ApiResponse<bool>> RemoveLogoAsync(string rowVersion, CancellationToken cancellationToken = default) =>
        RemoveBrandAssetAsync(false, rowVersion, cancellationToken);

    public Task<ApiResponse<bool>> RemoveFaviconAsync(string rowVersion, CancellationToken cancellationToken = default) =>
        RemoveBrandAssetAsync(true, rowVersion, cancellationToken);

    public async Task<ApiResponse<SubdomainAvailabilityDto>> CheckSubdomainAvailabilityAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<SubdomainAvailabilityDto>.ErrorResponse("Tenant access is required.", 403);
        var value = subdomain?.Trim().ToLowerInvariant() ?? "";
        var result = new SubdomainAvailabilityDto { Subdomain = value };
        if (value.Length is < 3 or > 100 || !Regex.IsMatch(value, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
        {
            result.Reason = "Use 3–100 lowercase letters, numbers and internal hyphens.";
            return ApiResponse<SubdomainAvailabilityDto>.SuccessResponse(result);
        }
        if (ReservedSubdomains.Contains(value))
        {
            result.Reason = "Subdomain is reserved.";
            return ApiResponse<SubdomainAvailabilityDto>.SuccessResponse(result);
        }
        result.IsValid = true;
        result.IsAvailable = !await _tenants.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.Subdomain == value && x.Id != _user.TenantId, cancellationToken);
        if (!result.IsAvailable) result.Reason = "Subdomain is already registered.";
        return ApiResponse<SubdomainAvailabilityDto>.SuccessResponse(result);
    }

    public async Task<ApiResponse<TenantDto>> UpdateSubdomainAsync(UpdateTenantSubdomainRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<TenantDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request == null) return ApiResponse<TenantDto>.ErrorResponse("A subdomain is required.");
        var availability = await CheckSubdomainAvailabilityAsync(request.Subdomain, cancellationToken);
        if (availability.Data?.IsAvailable != true)
            return ApiResponse<TenantDto>.ErrorResponse(availability.Data?.Reason ?? "Subdomain is not available.", 409);
        try
        {
            var tenant = await GetTenantAsync(cancellationToken);
            if (tenant == null) return ApiResponse<TenantDto>.ErrorResponse("Institution not found.", 404);
            if (!MatchesVersion(tenant.RowVersion, request.RowVersion))
                return ApiResponse<TenantDto>.ErrorResponse("Profile changed. Reload and retry.", 409);
            var changed = !string.Equals(tenant.Subdomain, availability.Data.Subdomain, StringComparison.Ordinal);
            tenant.Subdomain = availability.Data.Subdomain;
            tenant.UpdatedAt = DateTime.UtcNow; tenant.UpdatedBy = _user.UserId;
            if (changed) await RecordChangeAsync(tenant, "TenantSubdomainUpdated", ["Subdomain"]);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<TenantDto>.SuccessResponse(await MapAsync(tenant, cancellationToken));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<TenantDto>.ErrorResponse("Profile changed. Reload and retry.", 409); }
        catch (DbUpdateException) { return ApiResponse<TenantDto>.ErrorResponse("Subdomain is already taken.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Subdomain update failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<TenantDto>.ErrorResponse("Subdomain could not be saved.", 500);
        }
    }

    private async Task<ApiResponse<string>> UploadBrandAssetAsync(PrivateFileUploadDto file, bool favicon, CancellationToken ct)
    {
        if (!CanManage()) return ApiResponse<string>.ErrorResponse("Tenant administrator access is required.", 403);
        if (file == null || file.Content == Stream.Null || file.Length <= 0 ||
            file.Length > Math.Max(1, _settings.MaxFileSizeMb) * 1024L * 1024L)
            return ApiResponse<string>.ErrorResponse("Image size is invalid.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(ext) ||
            !new[] { "image/png", "image/jpeg", "image/webp" }.Contains(file.ContentType.ToLowerInvariant()) ||
            (favicon && ext == ".webp"))
            return ApiResponse<string>.ErrorResponse("Unsupported image type.");
        var form = new FormFile(file.Content, 0, file.Length, "file", Path.GetFileName(file.FileName))
        {
            Headers = new HeaderDictionary(),
            ContentType = file.ContentType
        };
        if (!_storage.ValidateFile(form)) return ApiResponse<string>.ErrorResponse("Image content is invalid.");
        string? uploaded = null;
        try
        {
            var tenant = await GetTenantAsync(ct);
            if (tenant == null) return ApiResponse<string>.ErrorResponse("Institution not found.", 404);
            var result = await _storage.UploadAsync(form, $"tenants/{tenant.Id}/branding");
            if (!result.Success || string.IsNullOrWhiteSpace(result.FileUrl))
                return ApiResponse<string>.ErrorResponse(result.ErrorMessage ?? "Image could not be uploaded.");
            uploaded = result.FileUrl;
            var previous = favicon ? tenant.FaviconUrl : tenant.LogoUrl;
            if (favicon) tenant.FaviconUrl = uploaded;
            else tenant.LogoUrl = uploaded;
            tenant.UpdatedAt = DateTime.UtcNow; tenant.UpdatedBy = _user.UserId;
            await RecordChangeAsync(tenant, favicon ? "TenantFaviconUploaded" : "TenantLogoUploaded",
                [favicon ? "Favicon" : "Logo"]);
            await _unitOfWork.SaveChangesAsync(ct);
            if (!string.IsNullOrEmpty(previous) && previous != uploaded &&
                !await _storage.DeleteAsync(previous))
                _logger.LogWarning("Previous branding file could not be deleted for {TenantId}", tenant.Id);
            return ApiResponse<string>.SuccessResponse(uploaded);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (uploaded != null) await _storage.DeleteAsync(uploaded);
            return ApiResponse<string>.ErrorResponse("Branding changed. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (uploaded != null) await _storage.DeleteAsync(uploaded);
            _logger.LogError(ex, "Branding upload failed for {TenantId}", _user.TenantId);
            return ApiResponse<string>.ErrorResponse("Brand image could not be saved.", 500);
        }
    }

    private async Task<ApiResponse<bool>> RemoveBrandAssetAsync(bool favicon, string rowVersion, CancellationToken ct)
    {
        if (!CanManage()) return ApiResponse<bool>.ErrorResponse("Tenant administrator access is required.", 403);
        try
        {
            var tenant = await GetTenantAsync(ct);
            if (tenant == null) return ApiResponse<bool>.ErrorResponse("Institution not found.", 404);
            if (!MatchesVersion(tenant.RowVersion, rowVersion))
                return ApiResponse<bool>.ErrorResponse("Branding changed. Reload and retry.", 409);
            var previous = favicon ? tenant.FaviconUrl : tenant.LogoUrl;
            if (previous == null) return ApiResponse<bool>.SuccessResponse(true);
            if (favicon) tenant.FaviconUrl = null;
            else tenant.LogoUrl = null;
            tenant.UpdatedAt = DateTime.UtcNow; tenant.UpdatedBy = _user.UserId;
            await RecordChangeAsync(tenant, favicon ? "TenantFaviconRemoved" : "TenantLogoRemoved",
                [favicon ? "Favicon" : "Logo"]);
            await _unitOfWork.SaveChangesAsync(ct);
            if (!await _storage.DeleteAsync(previous))
                _logger.LogWarning("Previous branding file could not be deleted for {TenantId}", tenant.Id);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<bool>.ErrorResponse("Branding changed. Reload and retry.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Branding removal failed for {TenantId}", _user.TenantId);
            return ApiResponse<bool>.ErrorResponse("Branding could not be updated.", 500);
        }
    }


    /// <summary>Tenant-scoped, privacy-safe history of configuration changes.</summary>
    public async Task<ApiResponse<PagedResult<TenantProfileChangeHistoryDto>>> GetChangeHistoryAsync(
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<PagedResult<TenantProfileChangeHistoryDto>>.ErrorResponse(
                "Tenant administrator access is required.", 403);
        page = Math.Clamp(page, 1, 10000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var tenantId = _user.TenantId;
        var query = _auditLogs.GetQueryable().IgnoreQueryFilters().AsNoTracking().Where(x =>
            !x.IsDeleted && x.TenantId == tenantId && x.EntityName == nameof(Tenant) &&
            x.EntityId == tenantId &&
            (x.Action == "TenantProfileUpdated" || x.Action == "TenantRegionalSettingsUpdated" ||
             x.Action == "TenantSubdomainUpdated" || x.Action == "TenantLogoUploaded" ||
             x.Action == "TenantLogoRemoved" || x.Action == "TenantFaviconUploaded" ||
             x.Action == "TenantFaviconRemoved"));
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TenantProfileChangeHistoryDto
            {
                Id = x.Id, Action = x.Action, ChangedFields = x.NewValue ?? "",
                ActorId = x.UserId, ActorName = x.UserName, OccurredAt = x.OccurredAt
            }).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<TenantProfileChangeHistoryDto>>.SuccessResponse(new()
        {
            Items = rows, TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    private Task RecordChangeAsync(Tenant tenant, string action, IReadOnlyCollection<string> fields)
    {
        var now = DateTime.UtcNow;
        var actor = _user.FullName;
        var ip = _user.IpAddress;
        return _auditLogs.AddAsync(new AuditLog
        {
            TenantId = tenant.Id, EntityName = nameof(Tenant), EntityId = tenant.Id,
            Action = action, NewValue = string.Join(", ", fields),
            UserId = _user.UserId, UserName = actor is { Length: > 200 } ? actor[..200] : actor,
            IpAddress = ip is { Length: > 100 } ? ip[..100] : ip,
            Endpoint = "/api/tenant-profile", RequestId = Guid.NewGuid().ToString("D"),
            OccurredAt = now, CreatedAt = now, CreatedBy = _user.UserId
        });
    }

    private Task<Tenant?> GetTenantAsync(CancellationToken ct) =>
        _tenants.GetQueryable().FirstOrDefaultAsync(x => x.Id == _user.TenantId, ct);

    private async Task<TenantDto> MapAsync(Tenant tenant, CancellationToken ct)
    {
        var name = tenant.InstitutionTypeDefinitionId.HasValue ?
            await _institutionTypes.GetQueryable().AsNoTracking()
                .Where(x => x.Id == tenant.InstitutionTypeDefinitionId.Value)
                .Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var domain = await _domains.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && x.IsActive && x.IsPrimary)
            .Select(x => x.HostName).FirstOrDefaultAsync(ct);
        return new TenantDto
        {
            Id = tenant.Id, Reference = tenant.PublicId,
            InstitutionTypeDefinitionId = tenant.InstitutionTypeDefinitionId,
            InstitutionTypeName = name, Name = tenant.Name, LegalName = tenant.LegalName,
            RegistrationNumber = tenant.RegistrationNumber, Code = tenant.Code,
            Subdomain = tenant.Subdomain, PrimaryDomainHostName = domain,
            Email = tenant.Email, Phone = tenant.Phone, Address = tenant.Address,
            CountryCode = tenant.CountryCode, LogoUrl = tenant.LogoUrl, FaviconUrl = tenant.FaviconUrl,
            CurrencyCode = tenant.CurrencyCode, TimeZoneId = tenant.TimeZoneId,
            DefaultLanguage = tenant.DefaultLanguage, DateFormat = tenant.DateFormat, State = tenant.State,
            OnboardingStage = tenant.OnboardingStage, OnboardingCompletedAt = tenant.OnboardingCompletedAt,
            EmailVerifiedAt = tenant.EmailVerifiedAt, RowVersion = Convert.ToBase64String(tenant.RowVersion)
        };
    }

    private static bool MatchesVersion(byte[] stored, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return stored.AsSpan().SequenceEqual(Convert.FromBase64String(value)); }
        catch (FormatException) { return false; }
    }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && _user.IsTenantAdmin;
}
