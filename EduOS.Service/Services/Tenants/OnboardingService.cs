using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants;

public sealed class OnboardingService : IOnboardingService
{
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly ITenantModuleService _modules;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(IGenericRepository<Tenant> tenants, IGenericRepository<Campus> campuses,
        IGenericRepository<AcademicYear> years, ITenantSubscriptionRepository subscriptions,
        ITenantModuleService modules, IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<OnboardingService> logger)
    {
        _tenants = tenants;
        _campuses = campuses;
        _years = years;
        _subscriptions = subscriptions;
        _modules = modules;
        _uow = uow;
        _user = user;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<OnboardingStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<OnboardingStatusDto>.ErrorResponse("Tenant access is required.", 403);
        var tenant = await _tenants.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == _user.TenantId && !x.IsDeleted && x.State != TenantState.Closed, cancellationToken);
        if (tenant == null) return ApiResponse<OnboardingStatusDto>.ErrorResponse("Institution not found.", 404);
        var current = tenant.OnboardingStage;
        if (!Enum.IsDefined(current)) return ApiResponse<OnboardingStatusDto>.ErrorResponse("Invalid onboarding stage.", 409);
        var finished = IsCompleted(tenant);
        var stages = Enum.GetValues<OnboardingStage>().Where(x => x != OnboardingStage.Completed)
            .Select(stage => new OnboardingStageStatusDto
            {
                Stage = stage,
                Code = stage.ToString(),
                Name = StageName(stage),
                Description = StageDescription(stage),
                DisplayOrder = (int)stage,
                IsCurrent = !finished && stage == current,
                IsCompleted = finished || (int)stage < (int)current,
                IsLocked = !finished && (int)stage > (int)current,
                IsSkippable = CanSkip(stage)
            }).ToArray();
        var count = stages.Count(x => x.IsCompleted);
        var next = finished ? (OnboardingStage?)null : current;
        return ApiResponse<OnboardingStatusDto>.SuccessResponse(new OnboardingStatusDto
        {
            TenantId = tenant.Id,
            CurrentStage = current,
            IsComplete = finished,
            CompletedAt = tenant.OnboardingCompletedAt,
            Stages = stages,
            TotalStages = stages.Length,
            CompletedStages = count,
            ProgressPercentage = (int)Math.Round(count * 100.0 / stages.Length),
            NextStageCode = next?.ToString(),
            NextStageName = next.HasValue ? StageName(next.Value) : null
        });
    }

    public async Task<ApiResponse<bool>> AdvanceToStageAsync(OnboardingStage stage, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied();
        if (!Enum.IsDefined(stage)) return Failure("Unknown onboarding stage.", 400);
        return await ChangeStageAsync(stage, null, cancellationToken);
    }

    public async Task<ApiResponse<bool>> CompleteStageAsync(CompleteOnboardingStageRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied();
        if (request == null || !Enum.IsDefined(request.Stage) || request.Stage == OnboardingStage.Completed)
            return Failure("Valid current onboarding stage is required.");
        if (request.Skipped && !CanSkip(request.Stage))
            return Failure("This onboarding stage cannot be skipped.", 409);
        return await ChangeStageAsync((OnboardingStage)((int)request.Stage + 1), request, cancellationToken);
    }

    public async Task<ApiResponse<bool>> CompleteOnboardingAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied();
        return await ChangeStageAsync(OnboardingStage.Completed, null, cancellationToken);
    }

    private async Task<ApiResponse<bool>> ChangeStageAsync(OnboardingStage target,
        CompleteOnboardingStageRequestDto? request, CancellationToken ct)
    {
        if (!Enum.IsDefined(target)) return Failure("Unknown onboarding stage.");
        try
        {
            var tenant = await _tenants.GetQueryable()
                .FirstOrDefaultAsync(x => x.Id == _user.TenantId && !x.IsDeleted && x.State != TenantState.Closed, ct);
            if (tenant == null) return Failure("Institution not found.", 404);
            if (IsCompleted(tenant))
                return target == OnboardingStage.Completed
                    ? ApiResponse<bool>.SuccessResponse(true, "Onboarding already completed.")
                    : Failure("Onboarding is already completed.", 409);
            var current = tenant.OnboardingStage;
            if (!Enum.IsDefined(current) || current == OnboardingStage.Completed)
                return Failure("Invalid current onboarding stage. Contact support.", 409);
            if (request != null && request.Stage != current)
                return Failure("The submitted stage is no longer current. Reload onboarding.", 409);
            if (request == null && target == current)
                return ApiResponse<bool>.SuccessResponse(true);
            if ((int)target != (int)current + 1)
                return Failure("Onboarding stages must be completed in order.", 409);

            if (request == null || !request.Skipped)
            {
                var validation = await ValidateStageAsync(tenant, current, ct);
                if (!validation.Success) return validation;
            }
            if (target == OnboardingStage.Completed)
            {
                var required = await ValidateRequiredStagesAsync(tenant, ct);
                if (!required.Success) return required;
                tenant.OnboardingCompletedAt = _clock.GetUtcNow().UtcDateTime;
                if (tenant.State == TenantState.PendingVerification) tenant.State = TenantState.Active;
            }
            tenant.OnboardingStage = target;
            tenant.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            tenant.UpdatedBy = _user.UserId;
            _tenants.Update(tenant);
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Tenant {TenantId} onboarding advanced from {From} to {To}.",
                tenant.Id, current, target);
            return ApiResponse<bool>.SuccessResponse(true, target == OnboardingStage.Completed
                ? "Onboarding completed." : "Onboarding stage completed.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent onboarding update for tenant {TenantId}.", _user.TenantId);
            return Failure("Onboarding was updated elsewhere. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save onboarding for tenant {TenantId}.", _user.TenantId);
            return Failure("Could not save onboarding.", 500);
        }
    }

    private async Task<ApiResponse<bool>> ValidateRequiredStagesAsync(Tenant tenant, CancellationToken ct)
    {
        foreach (var stage in new[] { OnboardingStage.EmailVerification, OnboardingStage.InstitutionProfile,
            OnboardingStage.PlanSelection, OnboardingStage.Payment, OnboardingStage.CampusSetup,
            OnboardingStage.AcademicSetup, OnboardingStage.ModuleSetup, OnboardingStage.BrandingSetup })
        {
            var result = await ValidateStageAsync(tenant, stage, ct);
            if (!result.Success) return result;
        }
        return ApiResponse<bool>.SuccessResponse(true);
    }

    private async Task<ApiResponse<bool>> ValidateStageAsync(Tenant tenant, OnboardingStage stage, CancellationToken ct)
    {
        switch (stage)
        {
            case OnboardingStage.EmailVerification:
                if (!tenant.EmailVerifiedAt.HasValue) return Failure("Verify the institution email first.", 409);
                break;
            case OnboardingStage.InstitutionProfile:
                if (!tenant.InstitutionTypeDefinitionId.HasValue || tenant.InstitutionTypeDefinitionId <= 0 ||
                    string.IsNullOrWhiteSpace(tenant.Name) || string.IsNullOrWhiteSpace(tenant.Email))
                    return Failure("Complete the institution profile first.", 409);
                break;
            case OnboardingStage.PlanSelection:
            case OnboardingStage.Payment:
                var subscription = await _subscriptions.GetActiveByTenantAsync(tenant.Id, ct);
                if (subscription == null) return Failure("Select a subscription plan first.", 409);
                if (stage == OnboardingStage.Payment &&
                    (subscription.EndsAt <= _clock.GetUtcNow().UtcDateTime ||
                     subscription.State is not (SubscriptionState.Active or SubscriptionState.Trial)))
                    return Failure("Activate a valid subscription or trial before continuing.", 409);
                break;
            case OnboardingStage.CampusSetup:
                if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(
                    x => x.TenantId == tenant.Id && x.IsActive && !x.IsDeleted, ct))
                    return Failure("Create an active campus before continuing.", 409);
                break;
            case OnboardingStage.AcademicSetup:
                if (!await _years.GetQueryable().AsNoTracking().AnyAsync(
                    x => x.TenantId == tenant.Id && x.IsActive && !x.IsDeleted, ct))
                    return Failure("Create an active academic year before continuing.", 409);
                break;
            case OnboardingStage.ModuleSetup:
                var modules = await _modules.ValidateCurrentTenantSelectionAsync();
                if (!modules.Success) return Failure(modules.Message ?? "Module setup is incomplete.", modules.StatusCode);
                break;
            case OnboardingStage.BrandingSetup:
                if (string.IsNullOrWhiteSpace(tenant.Subdomain)) return Failure("Set the institution subdomain first.", 409);
                break;
            case OnboardingStage.GeneralSettings:
            case OnboardingStage.GatewaySetup:
                break;
            default:
                return Failure("Unknown onboarding stage.");
        }
        return ApiResponse<bool>.SuccessResponse(true);
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && _user.IsTenantAdmin;
    private static bool IsCompleted(Tenant tenant) =>
        tenant.OnboardingStage == OnboardingStage.Completed && tenant.OnboardingCompletedAt.HasValue;
    private static bool CanSkip(OnboardingStage stage) =>
        stage is OnboardingStage.GeneralSettings or OnboardingStage.GatewaySetup;
    private static ApiResponse<bool> Denied() => Failure("Tenant administrator access is required.", 403);
    private static ApiResponse<bool> Failure(string message, int status = 400) =>
        ApiResponse<bool>.ErrorResponse(message, status);

    private static string StageName(OnboardingStage stage) => stage switch
    {
        OnboardingStage.EmailVerification => "Email verification",
        OnboardingStage.InstitutionProfile => "Institution profile",
        OnboardingStage.PlanSelection => "Choose plan",
        OnboardingStage.Payment => "Payment",
        OnboardingStage.CampusSetup => "Campus setup",
        OnboardingStage.AcademicSetup => "Academic year",
        OnboardingStage.ModuleSetup => "Module selection",
        OnboardingStage.BrandingSetup => "Branding",
        OnboardingStage.GeneralSettings => "General settings",
        OnboardingStage.GatewaySetup => "Gateway setup",
        _ => "Completed"
    };

    private static string StageDescription(OnboardingStage stage) => stage switch
    {
        OnboardingStage.EmailVerification => "Verify the institution email address",
        OnboardingStage.InstitutionProfile => "Set up institution details",
        OnboardingStage.PlanSelection => "Select a subscription plan",
        OnboardingStage.Payment => "Activate payment or free trial",
        OnboardingStage.CampusSetup => "Configure at least one campus",
        OnboardingStage.AcademicSetup => "Set up the academic year",
        OnboardingStage.ModuleSetup => "Choose institution modules",
        OnboardingStage.BrandingSetup => "Set up institution branding",
        OnboardingStage.GeneralSettings => "Review timezone and language",
        OnboardingStage.GatewaySetup => "Optional messaging gateways",
        _ => string.Empty
    };
}
