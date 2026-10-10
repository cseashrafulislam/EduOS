using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Interfaces.Jobs;
using EduOS.Core.Settings;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace EduOS.Service.Services.Tenants;

/// <summary>
/// Canonical public institution registration. Profile, campus, academic and onboarding
/// stage mutations have separate service owners and are not duplicated here.
/// </summary>
public sealed class InstitutionOnboardingService : IInstitutionRegistrationService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<TenantMembership> _memberships;
    private readonly IGenericRepository<InstitutionTypeDefinition> _types;
    private readonly IGenericRepository<InstitutionTypeModule> _presets;
    private readonly IGenericRepository<TenantModule> _modules;
    private readonly IGenericRepository<LegalDocument> _documents;
    private readonly IGenericRepository<UserLegalAcceptance> _acceptances;
    private readonly IUnitOfWork _uow;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<InstitutionOnboardingService> _logger;
    private readonly string _portalDomain;
    private readonly TimeProvider _clock;

    public InstitutionOnboardingService(UserManager<ApplicationUser> users,
        IGenericRepository<Tenant> tenants, IGenericRepository<TenantMembership> memberships,
        IGenericRepository<InstitutionTypeDefinition> types,
        IGenericRepository<InstitutionTypeModule> presets, IGenericRepository<TenantModule> modules,
        IGenericRepository<LegalDocument> documents,
        IGenericRepository<UserLegalAcceptance> acceptances,
        IUnitOfWork uow, IBackgroundJobClient jobs, IOptions<TenantPortalSettings> portalSettings,
        TimeProvider clock, ILogger<InstitutionOnboardingService> logger)
    {
        _users = users; _tenants = tenants; _memberships = memberships; _types = types;
        _presets = presets; _modules = modules; _documents = documents; _acceptances = acceptances;
        _uow = uow; _jobs = jobs; _portalDomain = portalSettings.Value.BaseDomain;
        _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<InstitutionSignupResponseDto>> RegisterInstitutionAsync(
        InstitutionSignupRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.InstitutionName) ||
            request.InstitutionName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.OwnerName) || request.OwnerName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 200 ||
            !MailAddress.TryCreate(request.Email.Trim(), out var parsed) ||
            !string.Equals(parsed.Address, request.Email.Trim(), StringComparison.OrdinalIgnoreCase) ||
            request.Phone?.Length > 30 || request.InstitutionTypeDefinitionId is null or <= 0 ||
            request.Password == null || request.Password.Length is < 6 or > 128 ||
            request.Password != request.ConfirmPassword || !request.AgreeTerms)
            return Error<InstitutionSignupResponseDto>("Invalid registration details or terms acceptance.");

        var email = request.Email.Trim().ToLowerInvariant();
        var code = "TN-" + request.ClientRequestId.ToString("N").ToUpperInvariant();
        var name = request.InstitutionName.Trim();
        var ownerName = request.OwnerName.Trim();
        var now = _clock.GetUtcNow().UtcDateTime;
        var type = await _types.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == request.InstitutionTypeDefinitionId.Value && x.IsActive && !x.IsDeleted, cancellationToken);
        if (type == null) return Error<InstitutionSignupResponseDto>("Institution type is unavailable.", 404);
        var terms = await _documents.GetQueryable().AsNoTracking().Where(x =>
            x.IsActive && !x.IsDeleted && x.Code.StartsWith("TERMS") && x.EffectiveAt <= now)
            .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (terms == null)
            return Error<InstitutionSignupResponseDto>("Current terms are not configured. Registration cannot proceed.", 409);
        if (!Uri.CheckHostName(_portalDomain.Trim()).Equals(UriHostNameType.Dns))
            return Error<InstitutionSignupResponseDto>("Institution email verification domain is not configured.", 503);

        var replay = await FindReplayAsync(code, email, name, ownerName, type.Id, cancellationToken);
        if (replay != null) return ApiResponse<InstitutionSignupResponseDto>.SuccessResponse(replay,
            "Registration already received. Check your email.");

        if (await _users.FindByEmailAsync(email) != null)
            return Error<InstitutionSignupResponseDto>("An account already exists with this email.", 409);

        try
        {
            var created = await _uow.ExecuteInTransactionAsync(async token =>
            {
                // The full client key becomes a unique tenant code. No MAX()+1 or global
                // writable duplicate of the registration request is introduced.
                var prior = await _tenants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Code == code, token);
                if (prior != null) throw new RegistrationRejectedException("Registration request already exists.");
                var timestamp = _clock.GetUtcNow().UtcDateTime;
                var tenant = new Tenant
                {
                    PublicId = Guid.NewGuid(), Name = name, Code = code, Email = email,
                    Phone = Trim(request.Phone), InstitutionTypeDefinitionId = type.Id,
                    State = TenantState.PendingVerification,
                    OnboardingStage = OnboardingStage.EmailVerification,
                    CreatedAt = timestamp, CurrencyCode = "BDT",
                    TimeZoneId = "Asia/Dhaka", DefaultLanguage = "bn-BD"
                };
                await _tenants.AddAsync(tenant);
                await _uow.SaveChangesAsync(token);

                var account = new ApplicationUser
                {
                    PublicId = Guid.NewGuid(), UserName = email, Email = email,
                    FullName = ownerName, EmailConfirmed = false,
                    IsActive = true, CreatedAt = timestamp
                };
                var identity = await _users.CreateAsync(account, request.Password);
                if (!identity.Succeeded)
                    throw new RegistrationRejectedException(string.Join("; ",
                        identity.Errors.Select(x => x.Description)));
                var role = await _users.AddToRoleAsync(account, "TenantAdmin");
                if (!role.Succeeded)
                    throw new RegistrationRejectedException("Tenant administrator role is not configured.");

                await _memberships.AddAsync(new TenantMembership
                {
                    TenantId = tenant.Id, UserId = account.Id, IsOwner = true,
                    Status = MembershipStatus.Active, JoinedAt = timestamp,
                    CreatedAt = timestamp
                });
                await _acceptances.AddAsync(new UserLegalAcceptance
                {
                    TenantId = tenant.Id, UserId = account.Id, LegalDocumentId = terms.Id,
                    AcceptedAt = timestamp, CreatedAt = timestamp
                });
                var defaults = await _presets.GetQueryable().AsNoTracking().Where(x =>
                    x.InstitutionTypeDefinitionId == type.Id && (x.IsRequired || x.IsDefaultEnabled) &&
                    !x.IsDeleted).Select(x => x.ProductModuleId).Distinct().ToListAsync(token);
                foreach (var moduleId in defaults)
                    await _modules.AddAsync(new TenantModule
                    {
                        TenantId = tenant.Id, ProductModuleId = moduleId, IsEnabled = true,
                        EnabledAt = timestamp, CreatedAt = timestamp
                    });
                await _uow.SaveChangesAsync(token);
                return new InstitutionSignupResponseDto
                {
                    TenantReference = tenant.PublicId, UserReference = account.PublicId,
                    Email = email, EmailVerificationRequired = true
                };
            }, cancellationToken);

            try
            {
                var account = await _users.FindByEmailAsync(email);
                if (account == null) throw new InvalidOperationException("Created identity is not readable.");
                var token = await _users.GenerateEmailConfirmationTokenAsync(account);
                var link = "https://" + _portalDomain.Trim().ToLowerInvariant() +
                    "/api/institution-onboarding/verify-email?email=" +
                    Uri.EscapeDataString(email) + "&token=" + Uri.EscapeDataString(token);
                _jobs.Enqueue<IEmailJob>(job => job.SendVerificationEmailAsync(
                    email, name, ownerName, link));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Institution registration {RequestId} succeeded but verification email queueing failed",
                    request.ClientRequestId);
            }
            return ApiResponse<InstitutionSignupResponseDto>.SuccessResponse(created,
                "Registration completed. Verify your email to continue.");
        }
        catch (RegistrationRejectedException ex)
        {
            return Error<InstitutionSignupResponseDto>(ex.Message, 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Institution registration conflict {RequestId}", request.ClientRequestId);
            var existing = await FindReplayAsync(code, email, name, ownerName, type.Id, cancellationToken);
            return existing != null
                ? ApiResponse<InstitutionSignupResponseDto>.SuccessResponse(existing, "Registration already received.")
                : Error<InstitutionSignupResponseDto>("Institution registration conflicts with an existing account.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Institution registration failed {RequestId}", request.ClientRequestId);
            return Error<InstitutionSignupResponseDto>("Registration could not be completed.", 500);
        }
    }

    public async Task<ApiResponse<bool>> VerifyEmailAsync(string email, string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200 ||
            string.IsNullOrWhiteSpace(token) || token.Length > 4096)
            return Error<bool>("Email verification token is invalid.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var account = await _users.FindByEmailAsync(email.Trim());
                if (account == null || !account.IsActive || account.IsDeleted)
                    return Error<bool>("Email verification link is invalid.", 404);
                var matches = await (from link in _memberships.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                    join tenant in _tenants.GetQueryable().IgnoreQueryFilters()
                        on link.TenantId equals tenant.Id
                    where link.UserId == account.Id && link.IsOwner &&
                        link.Status == MembershipStatus.Active && !link.IsDeleted &&
                        tenant.Email == account.Email && tenant.State == TenantState.PendingVerification &&
                        !tenant.IsDeleted
                    select tenant.Id).Take(2).ToListAsync(ct);
                if (matches.Count != 1)
                    return Error<bool>("Owner institution not found or already verified.", 409);

                var checkedToken = await _users.ConfirmEmailAsync(account, token);
                if (!checkedToken.Succeeded) return Error<bool>("Verification token expired or invalid.", 400);
                var row = await _tenants.GetQueryable().IgnoreQueryFilters()
                    .FirstAsync(x => x.Id == matches[0], ct);
                row.EmailVerifiedAt ??= _clock.GetUtcNow().UtcDateTime;
                if (row.OnboardingStage == OnboardingStage.EmailVerification)
                    row.OnboardingStage = OnboardingStage.InstitutionProfile;
                row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                await _uow.SaveChangesAsync(ct);
                return ApiResponse<bool>.SuccessResponse(true, "Email verified.");
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error<bool>("Verification state changed. Retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Institution email verification conflict");
            return Error<bool>("Verification could not be saved.", 409);
        }
    }

    private async Task<InstitutionSignupResponseDto?> FindReplayAsync(string code, string email,
        string name, string owner, long typeId, CancellationToken ct)
    {
        var existing = await _tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.Code == code && !x.IsDeleted, ct);
        if (existing == null || existing.Email != email || existing.Name != name ||
            existing.InstitutionTypeDefinitionId != typeId ||
            existing.State is TenantState.Closed or TenantState.Suspended) return null;
        var account = await (from link in _memberships.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            join user in _users.Users.AsNoTracking() on link.UserId equals user.Id
            where link.TenantId == existing.Id && link.IsOwner &&
                link.Status == MembershipStatus.Active && user.Email == email &&
                user.FullName == owner && !link.IsDeleted && !user.IsDeleted
            select user).FirstOrDefaultAsync(ct);
        return account == null ? null : new InstitutionSignupResponseDto
        {
            TenantReference = existing.PublicId, UserReference = account.PublicId,
            Email = email, EmailVerificationRequired = !account.EmailConfirmed
        };
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ApiResponse<T> Error<T>(string text, int code = 400) =>
        ApiResponse<T>.ErrorResponse(text, code);
    private sealed class RegistrationRejectedException(string text) : Exception(text);
}
