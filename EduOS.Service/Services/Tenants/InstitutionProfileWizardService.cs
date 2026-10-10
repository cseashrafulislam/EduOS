using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants;

public sealed class InstitutionProfileWizardService : IInstitutionProfileWizardService
{
    private readonly ITenantProfileService _profile;
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<TenantMembership> _memberships;
    private readonly IGenericRepository<TenantSetting> _settings;
    private readonly IGenericRepository<InstitutionTypeDefinition> _types;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly ILogger<InstitutionProfileWizardService> _logger;

    public InstitutionProfileWizardService(ITenantProfileService profile,
        IGenericRepository<Tenant> tenants, IGenericRepository<TenantMembership> memberships,
        IGenericRepository<TenantSetting> settings,
        IGenericRepository<InstitutionTypeDefinition> types, UserManager<ApplicationUser> users,
        IUnitOfWork uow, ICurrentUserService user, ILogger<InstitutionProfileWizardService> logger)
    {
        _profile = profile; _tenants = tenants; _memberships = memberships;
        _settings = settings; _types = types; _users = users; _uow = uow;
        _user = user; _logger = logger;
    }

    public async Task<ApiResponse<InstitutionProfileWizardDto>> GetAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Error<InstitutionProfileWizardDto>("Tenant administrator required.", 403);
        var p = await _profile.GetProfileAsync(ct);
        if (!p.Success || p.Data == null)
            return Error<InstitutionProfileWizardDto>(p.Message ?? "Profile is unavailable.", p.StatusCode);
        var owner = await OwnerAsync(ct);
        if (owner == null) return Error<InstitutionProfileWizardDto>("Tenant owner not found.", 409);
        var type = p.Data.InstitutionTypeDefinitionId.HasValue
            ? await _types.GetQueryable().AsNoTracking().Where(x =>
                x.Id == p.Data.InstitutionTypeDefinitionId.Value)
                .Select(x => x.Code).FirstOrDefaultAsync(ct) : null;
        var setting = await _settings.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.Category == "Profile" && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        return ApiResponse<InstitutionProfileWizardDto>.SuccessResponse(new InstitutionProfileWizardDto
        {
            InstitutionName = p.Data.Name, InstitutionType = type ?? "",
            OwnerName = owner.FullName, OwnerEmail = owner.Email,
            OwnerPhone = Find(setting, "Profile.OwnerPhone"),
            OwnerDesignation = Find(setting, "Profile.OwnerDesignation"),
            Phone = p.Data.Phone, Email = p.Data.Email, Address = p.Data.Address,
            Website = Find(setting, "Profile.Website"), City = Find(setting, "Profile.City"),
            State = Find(setting, "Profile.State"), Country = Find(setting, "Profile.Country"),
            PostalCode = Find(setting, "Profile.PostalCode"), RowVersion = p.Data.RowVersion
        });
    }

    public async Task<ApiResponse<bool>> SaveAsync(InstitutionProfileWizardDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<bool>("Tenant administrator required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.InstitutionName) ||
            request.InstitutionName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.OwnerName) || request.OwnerName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.InstitutionType) ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.OwnerEmail?.Length > 200 || request.OwnerPhone?.Length > 30 ||
            request.OwnerDesignation?.Length > 150 || request.Website?.Length > 250 ||
            request.City?.Length > 150 || request.State?.Length > 150 ||
            request.Country?.Length > 150 || request.PostalCode?.Length > 50)
            return Error<bool>("Invalid institution profile or missing row version.");
        var before = await _profile.GetProfileAsync(ct);
        if (!before.Success || before.Data == null)
            return Error<bool>(before.Message ?? "Profile not found.", before.StatusCode);
        var type = await _types.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.Code == request.InstitutionType.Trim().ToUpperInvariant() && x.IsActive && !x.IsDeleted, ct);
        if (type == null || type.Id != before.Data.InstitutionTypeDefinitionId)
            return Error<bool>("Institution type changes require a controlled migration.", 409);
        if (!string.Equals(request.OwnerEmail ?? before.Data.Email, before.Data.Email,
            StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Email ?? before.Data.Email, before.Data.Email,
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
                var p = await _profile.UpdateProfileAsync(new UpdateTenantProfileRequestDto
                {
                    Name = request.InstitutionName.Trim(), Email = before.Data.Email,
                    InstitutionTypeDefinitionId = type.Id,
                    Phone = Trim(request.Phone), Address = Trim(request.Address),
                    CountryCode = before.Data.CountryCode, RowVersion = request.RowVersion
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
