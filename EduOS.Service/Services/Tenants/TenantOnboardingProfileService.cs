using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants;

public sealed class TenantOnboardingProfileService : ITenantOnboardingProfileService
{
    private readonly ITenantProfileService _profile;
    private readonly IGenericRepository<TenantMembership> _memberships;
    private readonly IGenericRepository<TenantSetting> _settings;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly ILogger<TenantOnboardingProfileService> _logger;

    public TenantOnboardingProfileService(ITenantProfileService profile,
        IGenericRepository<TenantMembership> memberships, IGenericRepository<TenantSetting> settings,
        UserManager<ApplicationUser> users, IUnitOfWork uow, ICurrentUserService user,
        ILogger<TenantOnboardingProfileService> logger)
    {
        _profile = profile; _memberships = memberships;
        _settings = settings; _users = users; _uow = uow;
        _user = user; _logger = logger;
    }

    public async Task<ApiResponse<TenantOnboardingProfileDto>> GetAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Error<TenantOnboardingProfileDto>("Tenant administrator required.", 403);
        var p = await _profile.GetProfileAsync(ct);
        if (!p.Success || p.Data == null)
            return Error<TenantOnboardingProfileDto>(p.Message ?? "Profile is unavailable.", p.StatusCode);
        var owner = await OwnerAsync(ct);
        if (owner == null) return Error<TenantOnboardingProfileDto>("Tenant owner not found.", 409);
        var setting = await _settings.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.Category == "Profile" && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        return ApiResponse<TenantOnboardingProfileDto>.SuccessResponse(new TenantOnboardingProfileDto
        {
            Name = p.Data.Name, InstitutionTypeDefinitionId = p.Data.InstitutionTypeDefinitionId,
            LegalName = p.Data.LegalName, RegistrationNumber = p.Data.RegistrationNumber,
            Code = p.Data.Code, Subdomain = p.Data.Subdomain,
            OwnerName = owner.FullName, OwnerEmail = owner.Email,
            OwnerPhone = Find(setting, "Profile.OwnerPhone"),
            OwnerDesignation = Find(setting, "Profile.OwnerDesignation"),
            Phone = p.Data.Phone, Email = p.Data.Email, Address = p.Data.Address,
            CountryCode = p.Data.CountryCode,
            Website = Find(setting, "Profile.Website"), City = Find(setting, "Profile.City"),
            State = Find(setting, "Profile.State"), Country = Find(setting, "Profile.Country"),
            PostalCode = Find(setting, "Profile.PostalCode"), RowVersion = p.Data.RowVersion
        });
    }

    public async Task<ApiResponse<bool>> SaveAsync(TenantOnboardingProfileDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<bool>("Tenant administrator required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.OwnerName) || request.OwnerName.Trim().Length > 200 ||
            request.InstitutionTypeDefinitionId is null or <= 0 ||
            string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 200 ||
            string.IsNullOrWhiteSpace(request.CountryCode) || request.CountryCode.Trim().Length is < 2 or > 10 ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.LegalName?.Length > 250 || request.RegistrationNumber?.Length > 100 ||
            request.OwnerEmail?.Length > 200 || request.OwnerPhone?.Length > 30 ||
            request.OwnerDesignation?.Length > 150 || request.Website?.Length > 250 ||
            request.City?.Length > 150 || request.State?.Length > 150 ||
            request.Country?.Length > 150 || request.PostalCode?.Length > 50)
            return Error<bool>("Invalid institution profile or missing row version.");
        var before = await _profile.GetProfileAsync(ct);
        if (!before.Success || before.Data == null)
            return Error<bool>(before.Message ?? "Profile not found.", before.StatusCode);
        if (request.InstitutionTypeDefinitionId != before.Data.InstitutionTypeDefinitionId)
            return Error<bool>("Institution type changes require a controlled migration.", 409);
        if (!string.Equals(request.Email, before.Data.Email,
            StringComparison.OrdinalIgnoreCase))
            return Error<bool>("Email changes require a separate verification workflow.", 409);
        if (request.Website != null && request.Website.Length > 0 &&
            (!Uri.TryCreate(request.Website, UriKind.Absolute, out var website) ||
             (website.Scheme != Uri.UriSchemeHttps && website.Scheme != Uri.UriSchemeHttp)))
            return Error<bool>("Website URL must use HTTP or HTTPS.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var owner = await OwnerAsync(token);
                if (owner == null) return Error<bool>("Owner account not found.", 409);
                if (request.OwnerEmail != null && !string.Equals(request.OwnerEmail, owner.Email,
                    StringComparison.OrdinalIgnoreCase))
                    return Error<bool>("Owner email changes require a separate verification workflow.", 409);
                var p = await _profile.UpdateProfileAsync(new UpdateTenantProfileRequestDto
                {
                    Name = request.Name.Trim(), Email = before.Data.Email,
                    LegalName = request.LegalName, RegistrationNumber = request.RegistrationNumber,
                    InstitutionTypeDefinitionId = request.InstitutionTypeDefinitionId,
                    Phone = Trim(request.Phone), Address = Trim(request.Address),
                    CountryCode = request.CountryCode.Trim(), RowVersion = request.RowVersion
                }, token);
                if (!p.Success) return Error<bool>(p.Message ?? "Profile changed.", p.StatusCode);
                if (owner.FullName != request.OwnerName.Trim())
                {
                    owner.FullName = request.OwnerName.Trim();
                    var changed = await _users.UpdateAsync(owner);
                    if (!changed.Succeeded) throw new ProfileWriteException("Owner identity could not be updated.");
                }
                var data = new Dictionary<string, string?>
                {
                    ["OwnerPhone"] = Trim(request.OwnerPhone),
                    ["OwnerDesignation"] = Trim(request.OwnerDesignation),
                    ["Website"] = Trim(request.Website), ["City"] = Trim(request.City),
                    ["State"] = Trim(request.State), ["Country"] = Trim(request.Country),
                    ["PostalCode"] = Trim(request.PostalCode)
                };
                var keys = data.Keys.Select(x => "Profile." + x).ToArray();
                var existing = await _settings.GetQueryable().Where(x =>
                    x.TenantId == _user.TenantId && keys.Contains(x.Key) && !x.IsDeleted)
                    .ToDictionaryAsync(x => x.Key, token);
                foreach (var pair in data)
                {
                    var key = "Profile." + pair.Key;
                    if (existing.TryGetValue(key, out var row))
                    {
                        row.Value = pair.Value ?? "";
                        row.UpdatedAt = DateTime.UtcNow;
                        row.UpdatedBy = _user.UserId;
                        _settings.Update(row);
                    }
                    else
                    {
                        await _settings.AddAsync(new TenantSetting
                        {
                            TenantId = _user.TenantId, Key = key, Category = "Profile",
                            Value = pair.Value ?? "", CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId
                        });
                    }
                }
                await _uow.SaveChangesAsync(token);
                return ApiResponse<bool>.SuccessResponse(true, "Institution profile saved.");
            }, ct);
        }
        catch (ProfileWriteException ex) { return Error<bool>(ex.Message, 409); }
        catch (DbUpdateConcurrencyException) { return Error<bool>("Profile changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Institution profile conflict tenant {TenantId}", _user.TenantId);
            return Error<bool>("Institution profile conflicts with another update.", 409);
        }
    }

    private Task<ApplicationUser?> OwnerAsync(CancellationToken ct) =>
        (from link in _memberships.GetQueryable().AsNoTracking()
            join owner in _users.Users on link.UserId equals owner.Id
            where link.TenantId == _user.TenantId && link.IsOwner &&
                link.Status == EduOS.Core.Enums.Domain.MembershipStatus.Active && !link.IsDeleted
            select owner).FirstOrDefaultAsync(ct);

    private bool CanManage() => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;
    private static string? Find(IDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;
    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private sealed class ProfileWriteException(string message) : Exception(message);
}
