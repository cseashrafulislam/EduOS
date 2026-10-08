using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Interfaces.Jobs;
using EduOS.Core.Settings;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Transactions;

namespace EduOS.Service.Services.Tenants;

public sealed class InstitutionOnboardingService : IInstitutionOnboardingService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<TenantSetting> _settings;
    private readonly IGenericRepository<TenantMembership> _memberships;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AdmissionIntakeForm> _forms;
    private readonly IGenericRepository<InstitutionTypeDefinition> _institutionTypes;
    private readonly IGenericRepository<InstitutionTypeModule> _presets;
    private readonly IGenericRepository<TenantModule> _modules;
    private readonly IGenericRepository<TenantSubscription> _subscriptions;
    private readonly IGenericRepository<SubscriptionPlan> _plans;
    private readonly ITenantModuleService _tenantModuleService;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly ILogger<InstitutionOnboardingService> _logger;
    private readonly string _baseDomain;

    public InstitutionOnboardingService(UserManager<ApplicationUser> users, IGenericRepository<Tenant> tenants,
        IGenericRepository<TenantSetting> settings, IGenericRepository<TenantMembership> memberships,
        IGenericRepository<Campus> campuses,
        IGenericRepository<AcademicYear> years, IGenericRepository<AcademicTerm> terms,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AdmissionIntakeForm> forms,
        IGenericRepository<InstitutionTypeDefinition> institutionTypes,
        IGenericRepository<InstitutionTypeModule> presets, IGenericRepository<TenantModule> modules,
        IGenericRepository<TenantSubscription> subscriptions, IGenericRepository<SubscriptionPlan> plans,
        ITenantModuleService tenantModuleService, IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        IOptions<TenantPortalSettings> portalSettings, ILogger<InstitutionOnboardingService> logger)
    {
        _users = users; _tenants = tenants; _settings = settings; _memberships = memberships; _campuses = campuses;
        _years = years; _terms = terms; _batches = batches; _forms = forms;
        _institutionTypes = institutionTypes; _presets = presets; _modules = modules;
        _subscriptions = subscriptions; _plans = plans; _tenantModuleService = tenantModuleService;
        _uow = unitOfWork; _user = currentUser; _logger = logger;
        _baseDomain = portalSettings.Value.BaseDomain;
    }

    public async Task<ApiResponse<InstitutionSignupResponseDto>> RegisterInstitutionAsync(InstitutionSignupRequestDto dto, string baseUrl)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.InstitutionName) || dto.InstitutionName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(dto.OwnerName) || dto.OwnerName.Trim().Length > 150 ||
            string.IsNullOrWhiteSpace(dto.Email) || dto.Email.Length > 200 ||
            string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6 ||
            dto.Password != dto.ConfirmPassword || !dto.AgreeTerms)
            return Fail<InstitutionSignupResponseDto>("Institution, owner, email, password and agreement are required.");
        var email = dto.Email.Trim().ToLowerInvariant();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            return Fail<InstitutionSignupResponseDto>("Email address is invalid.");
        var typeCode = dto.InstitutionType?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(typeCode)) return Fail<InstitutionSignupResponseDto>("Institution type is required.");
        var type = await _institutionTypes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.Code == typeCode && x.IsActive);
        if (type == null) return Fail<InstitutionSignupResponseDto>("Institution type is unavailable.");
        if (await _users.FindByEmailAsync(email) != null)
            return Fail<InstitutionSignupResponseDto>("An account already exists with this email.", 409);
        try
        {
            var strategy = _uow.CreateExecutionStrategy();
            var response = await strategy.ExecuteAsync(async () =>
            {
                await _uow.BeginTransactionAsync();
                var committed = false;
                try
                {
                    var now = DateTime.UtcNow;
                    var tenant = new Tenant
                    {
                        PublicId = Guid.NewGuid(), Name = dto.InstitutionName.Trim(),
                        Code = "TN-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
                        Email = email, Phone = Trim(dto.Phone), InstitutionTypeDefinitionId = type.Id,
                        State = TenantState.PendingVerification,
                        OnboardingStage = OnboardingStage.EmailVerification,
                        IsEmailVerified = false, IsOnboardingComplete = false,
                        CurrencyCode = "BDT", TimeZoneId = "Asia/Dhaka", DefaultLanguage = "bn-BD",
                        IsActive = true, CreatedAt = now
                    };
                    await _tenants.AddAsync(tenant);
                    await _uow.SaveChangesAsync();
                    var account = new ApplicationUser
                    {
                        UserName = email, Email = email, FullName = dto.OwnerName.Trim(),
                        EmailConfirmed = false, IsActive = true, CreatedAt = now
                    };
                    var created = await _users.CreateAsync(account, dto.Password);
                    if (!created.Succeeded)
                        return Fail<InstitutionSignupResponseDto>(string.Join("; ", created.Errors.Select(x => x.Description)));
                    var assigned = await _users.AddToRoleAsync(account, "TenantAdmin");
                    if (!assigned.Succeeded)
                        return Fail<InstitutionSignupResponseDto>("Tenant administrator role could not be assigned.");
                    await _memberships.AddAsync(new TenantMembership
                    {
                        TenantId = tenant.Id, UserId = account.Id, IsOwner = true,
                        Status = MembershipStatus.Active, JoinedAt = now, CreatedAt = now
                    });
                    await UpsertAsync(tenant.Id, "Profile", new Dictionary<string, string?>
                    {
                        ["OwnerName"] = dto.OwnerName.Trim(), ["OwnerEmail"] = email,
                        ["OwnerPhone"] = Trim(dto.Phone)
                    });
                    await ApplyPresetAsync(tenant.Id, type.Id);
                    await _uow.SaveChangesAsync();
                    await _uow.CommitTransactionAsync();
                    committed = true;
                    return Ok(new InstitutionSignupResponseDto
                    {
                        Success = true, Email = account.Email, TenantId = tenant.Id, UserId = account.Id,
                        Message = "Account created. Verify your email to continue."
                    });
                }
                finally
                {
                    if (!committed) await SafeRollbackAsync();
                }
            });
            if (response.Success && response.Data != null && response.Data.UserId.HasValue)
            {
                try
                {
                    var accountId = response.Data.UserId.Value;
                    var account = await _users.FindByIdAsync(accountId.ToString());
                    if (account != null)
                    {
                        var token = await _users.GenerateEmailConfirmationTokenAsync(account);
                        var trustedOrigin = TrustedOrigin();
                        var link = trustedOrigin + "/api/institution-onboarding/verify-email?email=" +
                            Uri.EscapeDataString(email) + "&token=" + Uri.EscapeDataString(token);
                        BackgroundJob.Enqueue<IEmailJob>(job => job.SendVerificationEmailAsync(
                            email, dto.InstitutionName.Trim(), dto.OwnerName.Trim(), link));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Verification notification could not be queued for tenant {TenantId}", response.Data.TenantId);
                }
            }
            return response;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Institution signup conflicts with an existing record");
            return Fail<InstitutionSignupResponseDto>("Institution or account reference already exists.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Institution registration failed");
            return Fail<InstitutionSignupResponseDto>("Registration could not be completed.", 500);
        }
    }

    public async Task<bool> VerifyEmailAsync(string email, string token, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var user = await _users.FindByEmailAsync(email);
            if (user == null) return false;
            var result = await _users.ConfirmEmailAsync(user, token);
            if (!result.Succeeded) return false;
            var matching = await (from membership in _memberships.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                join institution in _tenants.GetQueryable().IgnoreQueryFilters() on membership.TenantId equals institution.Id
                where membership.UserId == user.Id && membership.IsOwner &&
                    membership.Status == MembershipStatus.Active && institution.IsActive &&
                    institution.State != TenantState.Suspended && institution.State != TenantState.Closed &&
                    institution.Email == user.Email
                select institution.Id).Take(2).ToListAsync();
            if (matching.Count != 1) return false;
            var tenant = await _tenants.GetQueryable().IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == matching[0]);
            if (tenant == null) return false;
            tenant.IsEmailVerified = true;
            tenant.EmailVerifiedAt ??= DateTime.UtcNow;
            if (tenant.OnboardingStage == OnboardingStage.EmailVerification)
                tenant.OnboardingStage = OnboardingStage.InstitutionProfile;
            tenant.UpdatedAt = DateTime.UtcNow;
            await _uow.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email verification failed");
            return false;
        }
    }

    public async Task<ApiResponse<InstitutionProfileSetupDto?>> GetInstitutionProfileAsync()
    {
        if (!CanRead()) return Denied<InstitutionProfileSetupDto?>();
        var tenant = await CurrentTenantAsync();
        if (tenant == null) return Fail<InstitutionProfileSetupDto?>("Institution not found.", 404);
        var settings = await ReadProfileAsync(tenant.Id);
        var code = tenant.InstitutionTypeDefinitionId.HasValue
            ? await _institutionTypes.GetQueryable().AsNoTracking().Where(x =>
                x.Id == tenant.InstitutionTypeDefinitionId.Value).Select(x => x.Code).FirstOrDefaultAsync() : null;
        return Ok<InstitutionProfileSetupDto?>(new InstitutionProfileSetupDto
        {
            InstitutionName = tenant.Name, InstitutionType = code,
            OwnerName = Get(settings, "OwnerName") ?? string.Empty,
            OwnerEmail = Get(settings, "OwnerEmail"), OwnerPhone = Get(settings, "OwnerPhone"),
            OwnerDesignation = Get(settings, "OwnerDesignation"),
            Phone = tenant.Phone, Email = tenant.Email, Address = tenant.Address,
            Website = Get(settings, "Website"), City = Get(settings, "City"),
            State = Get(settings, "State"), Country = Get(settings, "Country"),
            PostalCode = Get(settings, "PostalCode")
        });
    }

    public async Task<ApiResponse<bool>> SaveInstitutionProfileAsync(InstitutionProfileSetupDto dto)
    {
        if (!CanManage()) return Denied<bool>();
        if (dto == null || string.IsNullOrWhiteSpace(dto.InstitutionName) || dto.InstitutionName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(dto.OwnerName) || dto.OwnerName.Trim().Length > 150 ||
            string.IsNullOrWhiteSpace(dto.InstitutionType))
            return Fail<bool>("Institution name, owner and type are required.");
        var code = dto.InstitutionType.Trim().ToUpperInvariant();
        var type = await _institutionTypes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.Code == code && x.IsActive);
        if (type == null) return Fail<bool>("Institution type is invalid.");
        try
        {
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return Fail<bool>("Institution not found.", 404);
            if (tenant.IsOnboardingComplete && tenant.InstitutionTypeDefinitionId != type.Id)
                return Fail<bool>("Changing an established institution type requires a controlled migration.", 409);
            tenant.Name = dto.InstitutionName.Trim();
            tenant.InstitutionTypeDefinitionId = type.Id;
            tenant.Phone = Trim(dto.Phone); tenant.Address = Trim(dto.Address);
            tenant.UpdatedAt = DateTime.UtcNow; tenant.UpdatedBy = _user.UserId;
            await UpsertAsync(tenant.Id, "Profile", new Dictionary<string, string?>
            {
                ["OwnerName"] = dto.OwnerName.Trim(), ["OwnerEmail"] = Trim(dto.OwnerEmail),
                ["OwnerPhone"] = Trim(dto.OwnerPhone), ["OwnerDesignation"] = Trim(dto.OwnerDesignation),
                ["Website"] = Trim(dto.Website), ["City"] = Trim(dto.City), ["State"] = Trim(dto.State),
                ["Country"] = Trim(dto.Country), ["PostalCode"] = Trim(dto.PostalCode)
            });
            await ApplyPresetAsync(tenant.Id, type.Id);
            await _uow.SaveChangesAsync();
            return Ok(true, "Institution profile saved.");
        }
        catch (DbUpdateException) { return Fail<bool>("Profile changed concurrently.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Institution profile save failed for tenant {TenantId}", _user.TenantId);
            return Fail<bool>("Institution profile could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<List<CampusListItemDto>>> GetCampusListAsync()
    {
        if (!CanRead()) return Denied<List<CampusListItemDto>>();
        var t = _user.TenantId;
        var rows = await _campuses.GetQueryable().AsNoTracking().Where(x => x.TenantId == t && x.IsActive)
            .OrderByDescending(x => x.IsHeadOffice).ThenBy(x => x.Name).Take(500).ToListAsync();
        var ids = rows.Select(x => "Campus." + x.Id + ".HeadName").ToArray();
        var headNames = await _settings.GetQueryable().AsNoTracking().Where(x => x.TenantId == t &&
            ids.Contains(x.Key)).ToDictionaryAsync(x => x.Key, x => x.Value);
        return Ok(rows.Select(x => new CampusListItemDto
        {
            Id = x.Id, Name = x.Name, Code = x.Code, Address = x.Address, Phone = x.Phone,
            IsHeadOffice = x.IsHeadOffice, IsActive = x.IsActive,
            HeadName = headNames.GetValueOrDefault("Campus." + x.Id + ".HeadName")
        }).ToList());
    }

    public async Task<ApiResponse<CampusSetupDto?>> GetCampusByIdAsync(long id)
    {
        if (!CanRead()) return Denied<CampusSetupDto?>();
        var row = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && x.IsActive);
        if (row == null) return Fail<CampusSetupDto?>("Campus not found.", 404);
        var headName = await _settings.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.Key == "Campus." + row.Id + ".HeadName").Select(x => x.Value).FirstOrDefaultAsync();
        return Ok<CampusSetupDto?>(new CampusSetupDto
        {
            Id = row.Id, Name = row.Name, Code = row.Code, Phone = row.Phone,
            Email = row.Email, Address = row.Address, HeadName = headName, IsHeadOffice = row.IsHeadOffice
        });
    }

    public async Task<ApiResponse<bool>> SaveCampusAsync(CampusSetupDto dto)
    {
        if (!CanManage()) return Denied<bool>();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Trim().Length > 150 ||
            dto.HeadName?.Length > 150) return Fail<bool>("Campus name or head name is invalid.");
        var t = _user.TenantId;
        try
        {
            using var scope = SerializableScope();
            var tenant = await CurrentTenantAsync();
            if (tenant == null) return Fail<bool>("Institution not found.", 404);
            var isNew = dto.Id is null or <= 0;
            var existing = isNew ? null : await _campuses.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == t && x.Id == dto.Id && x.IsActive);
            if (!isNew && existing == null) return Fail<bool>("Campus not found.", 404);
            var currentCount = await _campuses.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == t && x.IsActive);
            if (isNew)
            {
                var now = DateTime.UtcNow;
                var sub = await _subscriptions.GetQueryable().AsNoTracking().Where(x => x.TenantId == t &&
                    x.StartsAt <= now && x.EndsAt > now &&
                    (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                     x.State == SubscriptionState.Grace)).OrderByDescending(x => x.StartsAt)
                    .Select(x => (long?)x.SubscriptionPlanId).FirstOrDefaultAsync();
                var max = sub.HasValue ? await _plans.GetQueryable().AsNoTracking()
                    .Where(x => x.Id == sub.Value).Select(x => x.MaxCampuses).FirstOrDefaultAsync() : 1;
                if (max > 0 && currentCount >= max) return Fail<bool>("Subscription campus limit reached.", 409);
            }
            var code = string.IsNullOrWhiteSpace(dto.Code)
                ? existing?.Code ?? ("C-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant())
                : dto.Code.Trim().ToUpperInvariant();
            if (code.Length > 50 || await _campuses.GetQueryable().AnyAsync(x =>
                x.TenantId == t && x.Code == code && x.Id != (dto.Id ?? 0)))
                return Fail<bool>("Campus code is invalid or already registered.", 409);
            var row = existing ?? new Campus
            {
                TenantId = t, PublicId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId
            };
            row.Name = dto.Name.Trim(); row.Code = code; row.Phone = Trim(dto.Phone);
            row.Email = Trim(dto.Email); row.Address = Trim(dto.Address);
            row.IsHeadOffice = dto.IsHeadOffice || isNew && currentCount == 0;
            row.IsActive = true; row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
            if (row.IsHeadOffice)
            {
                var previous = await _campuses.GetQueryable().Where(x => x.TenantId == t &&
                    x.Id != (dto.Id ?? 0) && x.IsHeadOffice).ToListAsync();
                foreach (var x in previous) { x.IsHeadOffice = false; x.UpdatedAt = DateTime.UtcNow; x.UpdatedBy = _user.UserId; }
            }
            if (existing == null) await _campuses.AddAsync(row);
            await _uow.SaveChangesAsync();
            await UpsertAsync(t, "Campus", new Dictionary<string, string?>
            { [row.Id + ".HeadName"] = Trim(dto.HeadName) });
            await _uow.SaveChangesAsync();
            scope.Complete();
            return Ok(true, "Campus saved.");
        }
        catch (DbUpdateException) { return Fail<bool>("Campus change conflicts with another update.", 409); }
        catch (TransactionAbortedException) { return Fail<bool>("Concurrent campus configuration conflict.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Campus save failed for tenant {TenantId}", t);
            return Fail<bool>("Campus could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<bool>> DeleteCampusAsync(long id)
    {
        if (!CanManage()) return Denied<bool>();
        var t = _user.TenantId;
        try
        {
            using var scope = SerializableScope();
            var row = await _campuses.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == t && x.Id == id && x.IsActive);
            if (row == null) return Fail<bool>("Campus not found.", 404);
            if (await _years.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.CampusId == id) ||
                await _batches.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.CampusId == id) ||
                await _forms.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.CampusId == id))
                return Fail<bool>("Campus has academic or admission records and cannot be removed.", 409);
            row.IsActive = false; row.IsDeleted = true;
            row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
            if (row.IsHeadOffice)
            {
                var replacement = await _campuses.GetQueryable().Where(x => x.TenantId == t &&
                    x.Id != id && x.IsActive).OrderBy(x => x.Id).FirstOrDefaultAsync();
                if (replacement != null) replacement.IsHeadOffice = true;
            }
            await _uow.SaveChangesAsync();
            scope.Complete();
            return Ok(true, "Campus archived.");
        }
        catch (DbUpdateException) { return Fail<bool>("Campus is referenced by other records.", 409); }
    }

    public async Task<ApiResponse<List<AcademicYearListItemDto>>> GetAcademicYearListAsync()
    {
        if (!CanRead()) return Denied<List<AcademicYearListItemDto>>();
        var rows = await _years.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId && x.IsActive)
            .OrderByDescending(x => x.StartDate).Take(200).ToListAsync();
        return Ok(rows.Select(x => new AcademicYearListItemDto
        {
            Id = x.Id, Name = x.Name, IsCurrent = x.IsCurrent,
            StartDate = x.StartDate.ToDateTime(TimeOnly.MinValue),
            EndDate = x.EndDate.ToDateTime(TimeOnly.MinValue)
        }).ToList());
    }

    public async Task<ApiResponse<AcademicYearSetupDto?>> GetAcademicYearByIdAsync(long id)
    {
        if (!CanRead()) return Denied<AcademicYearSetupDto?>();
        var row = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && x.IsActive);
        return row == null ? Fail<AcademicYearSetupDto?>("Academic year not found.", 404) :
            Ok<AcademicYearSetupDto?>(new AcademicYearSetupDto
            {
                Id = row.Id, Name = row.Name, IsCurrent = row.IsCurrent,
                StartDate = row.StartDate.ToDateTime(TimeOnly.MinValue),
                EndDate = row.EndDate.ToDateTime(TimeOnly.MinValue)
            });
    }

    public async Task<ApiResponse<bool>> SaveAcademicYearAsync(AcademicYearSetupDto dto)
    {
        if (!CanManage()) return Denied<bool>();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Trim().Length > 100 ||
            dto.StartDate.Date >= dto.EndDate.Date)
            return Fail<bool>("Academic year dates or name are invalid.");
        var t = _user.TenantId;
        try
        {
            using var scope = SerializableScope();
            var start = DateOnly.FromDateTime(dto.StartDate.Date);
            var end = DateOnly.FromDateTime(dto.EndDate.Date);
            var existing = dto.Id is > 0 ? await _years.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == t && x.Id == dto.Id.Value && x.IsActive) : null;
            if (dto.Id is > 0 && existing == null) return Fail<bool>("Academic year not found.", 404);
            var name = dto.Name.Trim();
            if (await _years.GetQueryable().AnyAsync(x => x.TenantId == t && x.Name == name && x.Id != (dto.Id ?? 0)))
                return Fail<bool>("Academic year name is already in use.", 409);
            if (existing != null && await _terms.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == t && x.AcademicYearId == existing.Id &&
                (x.StartDate < start || x.EndDate > end)))
                return Fail<bool>("Existing terms must remain within the academic year.", 409);
            if (dto.IsCurrent)
            {
                var others = await _years.GetQueryable().Where(x => x.TenantId == t && x.IsCurrent &&
                    x.Id != (dto.Id ?? 0)).ToListAsync();
                foreach (var x in others) x.IsCurrent = false;
            }
            var row = existing ?? new AcademicYear
            {
                TenantId = t, Code = "AY-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId
            };
            row.Name = name; row.StartDate = start; row.EndDate = end;
            row.IsCurrent = dto.IsCurrent; row.IsActive = true;
            row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
            if (existing == null) await _years.AddAsync(row);
            await _uow.SaveChangesAsync();
            scope.Complete();
            return Ok(true, "Academic year saved.");
        }
        catch (DbUpdateException) { return Fail<bool>("Academic year conflicts with another record.", 409); }
        catch (TransactionAbortedException) { return Fail<bool>("Concurrent academic year update detected.", 409); }
    }

    public async Task<ApiResponse<bool>> DeleteAcademicYearAsync(long id)
    {
        if (!CanManage()) return Denied<bool>();
        var t = _user.TenantId;
        var row = await _years.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == t && x.Id == id && x.IsActive);
        if (row == null) return Fail<bool>("Academic year not found.", 404);
        if (await _terms.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.AcademicYearId == id) ||
            await _batches.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.AcademicYearId == id) ||
            await _forms.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.AcademicYearId == id))
            return Fail<bool>("Academic year has dependent records and cannot be removed.", 409);
        row.IsActive = false; row.IsDeleted = true;
        row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
        await _uow.SaveChangesAsync();
        return Ok(true, "Academic year archived.");
    }

    public async Task<ApiResponse<List<AcademicTermListItemDto>>> GetAcademicTermListAsync()
    {
        if (!CanRead()) return Denied<List<AcademicTermListItemDto>>();
        var t = _user.TenantId;
        var rows = await (from term in _terms.GetQueryable().AsNoTracking()
            join year in _years.GetQueryable().AsNoTracking() on term.AcademicYearId equals year.Id
            where term.TenantId == t && year.TenantId == t && term.IsActive
            orderby year.StartDate descending, term.StartDate
            select new { term, year.Name }).Take(300).ToListAsync();
        return Ok(rows.Select(x => new AcademicTermListItemDto
        {
            Id = x.term.Id, AcademicYearId = x.term.AcademicYearId,
            AcademicYearName = x.Name, Name = x.term.Name,
            StartDate = x.term.StartDate.ToDateTime(TimeOnly.MinValue),
            EndDate = x.term.EndDate.ToDateTime(TimeOnly.MinValue)
        }).ToList());
    }

    public async Task<ApiResponse<AcademicTermSetupDto?>> GetAcademicTermByIdAsync(long id)
    {
        if (!CanRead()) return Denied<AcademicTermSetupDto?>();
        var row = await _terms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && x.IsActive);
        return row == null ? Fail<AcademicTermSetupDto?>("Academic term not found.", 404) :
            Ok<AcademicTermSetupDto?>(new AcademicTermSetupDto
            {
                Id = row.Id, Name = row.Name, AcademicYearId = row.AcademicYearId,
                StartDate = row.StartDate.ToDateTime(TimeOnly.MinValue),
                EndDate = row.EndDate.ToDateTime(TimeOnly.MinValue)
            });
    }

    public async Task<ApiResponse<bool>> SaveAcademicTermAsync(AcademicTermSetupDto dto)
    {
        if (!CanManage()) return Denied<bool>();
        if (dto == null || dto.AcademicYearId <= 0 || string.IsNullOrWhiteSpace(dto.Name) ||
            dto.Name.Trim().Length > 100 || !dto.StartDate.HasValue || !dto.EndDate.HasValue ||
            dto.StartDate.Value.Date >= dto.EndDate.Value.Date)
            return Fail<bool>("Academic term requires a year, name and a valid date range.");
        var t = _user.TenantId;
        try
        {
            var start = DateOnly.FromDateTime(dto.StartDate.Value.Date);
            var end = DateOnly.FromDateTime(dto.EndDate.Value.Date);
            var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == t && x.Id == dto.AcademicYearId && x.IsActive);
            if (year == null) return Fail<bool>("Academic year not found.", 404);
            if (start < year.StartDate || end > year.EndDate)
                return Fail<bool>("Term dates must fit within the year.", 409);
            var name = dto.Name.Trim();
            if (await _terms.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t &&
                x.AcademicYearId == dto.AcademicYearId && x.Name == name && x.Id != (dto.Id ?? 0)))
                return Fail<bool>("Term name already exists in this year.", 409);
            var row = dto.Id is > 0 ? await _terms.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == t && x.Id == dto.Id.Value && x.IsActive) : null;
            if (dto.Id is > 0 && row == null) return Fail<bool>("Term not found.", 404);
            row ??= new AcademicTerm { TenantId = t, CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId };
            row.Name = name; row.AcademicYearId = dto.AcademicYearId;
            row.StartDate = start; row.EndDate = end; row.IsActive = true;
            row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
            if (dto.Id is not > 0) await _terms.AddAsync(row);
            await _uow.SaveChangesAsync();
            return Ok(true, "Academic term saved.");
        }
        catch (DbUpdateException) { return Fail<bool>("Term conflicts with existing records.", 409); }
    }

    public async Task<ApiResponse<bool>> DeleteAcademicTermAsync(long id)
    {
        if (!CanManage()) return Denied<bool>();
        var t = _user.TenantId;
        var row = await _terms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == t && x.Id == id && x.IsActive);
        if (row == null) return Fail<bool>("Academic term not found.", 404);
        if (await _batches.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.AcademicTermId == id) ||
            await _forms.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.AcademicTermId == id))
            return Fail<bool>("Academic term is referenced and cannot be removed.", 409);
        row.IsActive = false; row.IsDeleted = true;
        row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
        await _uow.SaveChangesAsync();
        return Ok(true, "Academic term archived.");
    }

    public async Task<ApiResponse<bool>> FinalCompleteAsync()
    {
        if (!CanManage()) return Denied<bool>();
        var t = _user.TenantId;
        var tenant = await CurrentTenantAsync();
        if (tenant == null) return Fail<bool>("Institution not found.", 404);
        if (tenant.IsOnboardingComplete) return Ok(true, "Onboarding already completed.");
        if (!tenant.IsEmailVerified || !tenant.InstitutionTypeDefinitionId.HasValue)
            return Fail<bool>("Verify email and complete the institution profile first.", 409);
        if (tenant.OnboardingStage != OnboardingStage.GatewaySetup)
            return Fail<bool>("Complete all onboarding steps before finishing.", 409);
        if (string.IsNullOrWhiteSpace(tenant.Subdomain))
            return Fail<bool>("Configure the institution subdomain.", 409);
        if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.IsActive && x.IsHeadOffice) ||
            !await _years.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t && x.IsActive))
            return Fail<bool>("At least one head campus and academic year are required.", 409);
        var now = DateTime.UtcNow;
        if (!await _subscriptions.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == t &&
            x.StartsAt <= now && x.EndsAt > now &&
            (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
            x.State == SubscriptionState.Grace)))
            return Fail<bool>("Select an active subscription plan first.", 409);
        var modules = await _tenantModuleService.ValidateCurrentTenantSelectionAsync();
        if (!modules.Success) return Fail<bool>(modules.Message, modules.StatusCode);
        tenant.State = TenantState.Active;
        tenant.IsOnboardingComplete = true;
        tenant.OnboardingStage = OnboardingStage.Completed;
        tenant.OnboardingCompletedAt = now; tenant.UpdatedAt = now; tenant.UpdatedBy = _user.UserId;
        try
        {
            await _uow.SaveChangesAsync();
            return Ok(true, "Institution onboarding completed.");
        }
        catch (DbUpdateConcurrencyException) { return Fail<bool>("Institution configuration changed. Reload and retry.", 409); }
    }

    private async Task<Tenant?> CurrentTenantAsync() =>
        await _tenants.GetQueryable().FirstOrDefaultAsync(x => x.Id == _user.TenantId && x.IsActive);
    private async Task<Dictionary<string, string>> ReadProfileAsync(long tenantId)
    {
        var data = await _settings.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.Category == "Profile").ToListAsync();
        return data.ToDictionary(x => x.Key, x => x.Value);
    }
    private async Task UpsertAsync(long tenantId, string category, IReadOnlyDictionary<string, string?> entries)
    {
        var keys = entries.Keys.Select(x => category + "." + x).ToArray();
        var rows = await _settings.GetQueryable().Where(x => x.TenantId == tenantId &&
            keys.Contains(x.Key)).ToDictionaryAsync(x => x.Key);
        var now = DateTime.UtcNow;
        foreach (var entry in entries)
        {
            var key = category + "." + entry.Key;
            var value = entry.Value ?? string.Empty;
            if (rows.TryGetValue(key, out var row))
            {
                row.Value = value; row.UpdatedAt = now; row.UpdatedBy = _user.IsAuthenticated ? _user.UserId : null;
            }
            else await _settings.AddAsync(new TenantSetting
            {
                TenantId = tenantId, Key = key, Value = value, Category = category,
                CreatedAt = now, CreatedBy = _user.IsAuthenticated ? _user.UserId : null
            });
        }
    }
    private async Task ApplyPresetAsync(long tenantId, long typeId)
    {
        var configured = await _presets.GetQueryable().AsNoTracking().Where(x =>
            x.InstitutionTypeDefinitionId == typeId && (x.IsRequired || x.IsDefaultEnabled)).ToListAsync();
        var existing = await _modules.GetQueryable().Where(x => x.TenantId == tenantId).ToListAsync();
        var byModule = existing.ToDictionary(x => x.ProductModuleId);
        var now = DateTime.UtcNow;
        foreach (var preset in configured)
        {
            if (byModule.TryGetValue(preset.ProductModuleId, out var selected))
            {
                if (preset.IsRequired && !selected.IsEnabled)
                { selected.IsEnabled = true; selected.EnabledAt = now; selected.DisabledAt = null; }
            }
            else await _modules.AddAsync(new TenantModule
            {
                TenantId = tenantId, ProductModuleId = preset.ProductModuleId,
                IsEnabled = true, EnabledAt = now, CreatedAt = now
            });
        }
    }
    private string TrustedOrigin()
    {
        var host = (_baseDomain ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        if (host.Length < 4 || host.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '.' or '-')))
            throw new InvalidOperationException("Tenant portal domain is not configured safely.");
        return "https://app." + host;
    }
    private async Task SafeRollbackAsync()
    {
        try { await _uow.RollbackTransactionAsync(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Transaction cleanup was already completed."); }
    }
    private static TransactionScope SerializableScope() =>
        new(TransactionScopeOption.Required, new TransactionOptions
        { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && _user.IsTenantAdmin;
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Get(IReadOnlyDictionary<string, string> values, string field) =>
        values.GetValueOrDefault("Profile." + field) is string value && value.Length > 0 ? value : null;
    private static ApiResponse<T> Ok<T>(T data, string message = "Success") =>
        ApiResponse<T>.SuccessResponse(data, message);
    private static ApiResponse<T> Fail<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static ApiResponse<T> Denied<T>() => Fail<T>("Tenant administrator access is required.", 403);
}
