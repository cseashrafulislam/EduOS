using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

public class TenantModuleService : ITenantModuleService
{
    private readonly IGenericRepository<Tenant> _tenantRepository;
    private readonly IGenericRepository<ProductModule> _moduleRepository;
    private readonly IGenericRepository<InstitutionTypeModule> _presetModuleRepository;
    private readonly IGenericRepository<TenantModule> _tenantModuleRepository;
    private readonly IGenericRepository<ProductModuleFeature> _moduleFeatureRepository;
    private readonly IGenericRepository<PlanFeature> _planFeatureRepository;
    private readonly IGenericRepository<TenantSubscription> _subscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<TenantModuleService> _logger;

    public TenantModuleService(
        IGenericRepository<Tenant> tenantRepository,
        IGenericRepository<ProductModule> moduleRepository,
        IGenericRepository<InstitutionTypeModule> presetModuleRepository,
        IGenericRepository<TenantModule> tenantModuleRepository,
        IGenericRepository<ProductModuleFeature> moduleFeatureRepository,
        IGenericRepository<PlanFeature> planFeatureRepository,
        IGenericRepository<TenantSubscription> subscriptionRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ILogger<TenantModuleService> logger)
    {
        _tenantRepository = tenantRepository;
        _moduleRepository = moduleRepository;
        _presetModuleRepository = presetModuleRepository;
        _tenantModuleRepository = tenantModuleRepository;
        _moduleFeatureRepository = moduleFeatureRepository;
        _planFeatureRepository = planFeatureRepository;
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<List<TenantModuleDto>>> GetCurrentTenantModulesAsync()
    {
        if (_currentUser.TenantId <= 0)
            return ApiResponse<List<TenantModuleDto>>.ErrorResponse("Tenant context is required", 403);

        try
        {
            var items = await BuildModuleStateAsync(_currentUser.TenantId);
            return ApiResponse<List<TenantModuleDto>>.SuccessResponse(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate modules for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<List<TenantModuleDto>>.ErrorResponse("Failed to load tenant modules", 500);
        }
    }

    public async Task<ApiResponse<TenantModuleDto>> UpdateCurrentTenantModuleAsync(
        string moduleCode,
        UpdateTenantModuleRequestDto request)
    {
        if (!_currentUser.IsTenantAdmin && !_currentUser.IsSuperAdmin)
            return ApiResponse<TenantModuleDto>.ErrorResponse("Tenant administrator access is required", 403);

        if (_currentUser.TenantId <= 0)
            return ApiResponse<TenantModuleDto>.ErrorResponse("Tenant context is required", 403);

        if (!request.IsEnabled.HasValue)
            return ApiResponse<TenantModuleDto>.ErrorResponse("Module enabled state is required");

        if (!TryNormalizeCode(moduleCode, out var normalizedCode))
            return ApiResponse<TenantModuleDto>.ErrorResponse("Module code is invalid");

        if (request.EffectiveFromUtc.HasValue && request.EffectiveUntilUtc.HasValue
            && request.EffectiveUntilUtc <= request.EffectiveFromUtc)
        {
            return ApiResponse<TenantModuleDto>
                .ErrorResponse("Effective end must be after effective start");
        }

        try
        {
            var tenantId = _currentUser.TenantId;
            var tenant = await _tenantRepository.GetByIdAsync(tenantId);
            if (tenant == null)
                return ApiResponse<TenantModuleDto>.ErrorResponse("Tenant not found", 404);

            var module = await _moduleRepository.FirstOrDefaultAsync(x =>
                x.Code == normalizedCode && x.IsActive);
            if (module == null)
                return ApiResponse<TenantModuleDto>.ErrorResponse("Module not found", 404);

            InstitutionTypeModule? presetMapping = null;
            if (tenant.InstitutionTypeDefinitionId.HasValue)
            {
                presetMapping = await _presetModuleRepository.FirstOrDefaultAsync(x =>
                    x.InstitutionTypeDefinitionId == tenant.InstitutionTypeDefinitionId.Value
                    && x.ProductModuleId == module.Id);
            }

            if (!request.IsEnabled.Value && (module.IsCore || presetMapping?.IsRequired == true))
            {
                return ApiResponse<TenantModuleDto>
                    .ErrorResponse("A required module cannot be disabled", 409);
            }

            if (request.IsEnabled.Value && !await IsModuleIncludedInCurrentPlanAsync(tenantId, module))
            {
                return ApiResponse<TenantModuleDto>
                    .ErrorResponse("The active subscription does not include this module", 403);
            }

            var tenantModule = await _tenantModuleRepository.FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.ProductModuleId == module.Id);
            var now = DateTime.UtcNow;

            if (tenantModule == null)
            {
                tenantModule = new TenantModule
                {
                    TenantId = tenantId,
                    ProductModuleId = module.Id,
                    CreatedAt = now
                };
                await _tenantModuleRepository.AddAsync(tenantModule);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.RowVersion))
                {
                    return ApiResponse<TenantModuleDto>.ErrorResponse(
                        "Reload the module selection before changing it.", 428);
                }

                if (!MatchesExpectedRowVersion(tenantModule, request.RowVersion))
                {
                    return ApiResponse<TenantModuleDto>
                        .ErrorResponse("The module was changed by another request. Reload and try again.", 409);
                }
                _tenantModuleRepository.Update(tenantModule);
            }

            tenantModule.IsEnabled = request.IsEnabled.Value;
            tenantModule.ActivationSource = TenantModuleActivationSource.TenantChoice;
            tenantModule.EffectiveFromUtc = request.EffectiveFromUtc;
            tenantModule.EffectiveUntilUtc = request.EffectiveUntilUtc;
            tenantModule.UpdatedAt = now;
            tenantModule.UpdatedBy = _currentUser.UserId;

            if (tenantModule.IsEnabled)
            {
                tenantModule.EnabledAt = now;
                tenantModule.DisabledAt = null;
                tenantModule.DisabledReason = null;
            }
            else
            {
                tenantModule.DisabledAt = now;
                tenantModule.DisabledReason = request.DisabledReason?.Trim();
            }

            await _unitOfWork.SaveChangesAsync();

            var updated = (await BuildModuleStateAsync(tenantId))
                .Single(x => x.Code == normalizedCode);
            return ApiResponse<TenantModuleDto>.SuccessResponse(updated, "Module selection updated");
        }
        catch (FormatException)
        {
            return ApiResponse<TenantModuleDto>.ErrorResponse("Row version is invalid", 400);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent tenant module update for {Code}", normalizedCode);
            return ApiResponse<TenantModuleDto>
                .ErrorResponse("The module was changed by another request. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting tenant module update for {Code}", normalizedCode);
            return ApiResponse<TenantModuleDto>
                .ErrorResponse("The module was changed by another request. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update module {Code} for tenant {TenantId}", normalizedCode, _currentUser.TenantId);
            return ApiResponse<TenantModuleDto>.ErrorResponse("Failed to update module", 500);
        }
    }

    public async Task<ApiResponse<bool>> ValidateCurrentTenantSelectionAsync()
    {
        if (_currentUser.TenantId <= 0)
            return ApiResponse<bool>.ErrorResponse("Tenant context is required", 403);

        try
        {
            var modules = await BuildModuleStateAsync(_currentUser.TenantId);
            if (modules.Count == 0)
                return ApiResponse<bool>.ErrorResponse("No active modules are configured", 409);

            var unavailableRequired = modules
                .Where(x => x.IsRequiredForInstitution && !x.IsAvailable)
                .Select(x => x.Code)
                .ToArray();
            if (unavailableRequired.Length > 0)
            {
                _logger.LogWarning(
                    "Tenant {TenantId} cannot complete module setup because required modules are unavailable: {Codes}",
                    _currentUser.TenantId,
                    string.Join(",", unavailableRequired));
                return ApiResponse<bool>.ErrorResponse(
                    "One or more required modules are unavailable in the current plan", 409);
            }

            return ApiResponse<bool>.SuccessResponse(true, "Module selection is valid");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate modules for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<bool>.ErrorResponse("Failed to validate tenant modules", 500);
        }
    }

    public async Task<bool> IsCurrentTenantModuleAvailableAsync(string moduleCode)
    {
        if (_currentUser.TenantId <= 0 || !TryNormalizeCode(moduleCode, out var normalizedCode))
            return false;

        try
        {
            var state = await BuildModuleStateAsync(_currentUser.TenantId);
            return state.Any(x => x.Code == normalizedCode && x.IsAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module authorization evaluation failed for {Code}", normalizedCode);
            return false;
        }
    }

    public async Task<Result> ApplyInstitutionPresetAsync(
        long tenantId,
        long institutionTypeDefinitionId)
    {
        if (tenantId <= 0 || institutionTypeDefinitionId <= 0)
            return Result.Failure("Tenant and institution type are required");

        try
        {
            var presetModules = await _presetModuleRepository.GetQueryable()
                .AsNoTracking()
                .Where(x => x.InstitutionTypeDefinitionId == institutionTypeDefinitionId
                            && (x.IsRequired || x.IsEnabledByDefault))
                .ToListAsync();
            var desiredIds = presetModules.Select(x => x.ProductModuleId).ToHashSet();
            var existing = await _tenantModuleRepository.GetQueryable()
                .Where(x => x.TenantId == tenantId)
                .ToListAsync();
            var existingByModule = existing.ToDictionary(x => x.ProductModuleId);
            var now = DateTime.UtcNow;

            foreach (var mapping in presetModules)
            {
                if (existingByModule.TryGetValue(mapping.ProductModuleId, out var tenantModule))
                {
                    if (mapping.IsRequired && !tenantModule.IsEnabled)
                    {
                        tenantModule.IsEnabled = true;
                        tenantModule.ActivationSource = TenantModuleActivationSource.InstitutionPreset;
                        tenantModule.EnabledAt = now;
                        tenantModule.DisabledAt = null;
                        tenantModule.DisabledReason = null;
                        tenantModule.UpdatedAt = now;
                        _tenantModuleRepository.Update(tenantModule);
                    }
                    continue;
                }

                await _tenantModuleRepository.AddAsync(new TenantModule
                {
                    TenantId = tenantId,
                    ProductModuleId = mapping.ProductModuleId,
                    IsEnabled = true,
                    ActivationSource = TenantModuleActivationSource.InstitutionPreset,
                    EnabledAt = now,
                    CreatedAt = now
                });
            }

            foreach (var previousPresetModule in existing.Where(x =>
                         x.ActivationSource == TenantModuleActivationSource.InstitutionPreset
                         && !desiredIds.Contains(x.ProductModuleId)
                         && x.IsEnabled))
            {
                previousPresetModule.IsEnabled = false;
                previousPresetModule.DisabledAt = now;
                previousPresetModule.DisabledReason = "INSTITUTION_PRESET_CHANGED";
                previousPresetModule.UpdatedAt = now;
                _tenantModuleRepository.Update(previousPresetModule);
            }

            await _unitOfWork.SaveChangesAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to apply institution preset {InstitutionTypeId} to tenant {TenantId}",
                institutionTypeDefinitionId,
                tenantId);
            return Result.Failure("Failed to apply institution module preset");
        }
    }

    private async Task<List<TenantModuleDto>> BuildModuleStateAsync(long tenantId)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId)
                     ?? throw new InvalidOperationException("Tenant not found");
        var now = DateTime.UtcNow;
        var modules = await _moduleRepository.GetQueryable()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
        var selections = await _tenantModuleRepository.GetQueryable()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.ProductModuleId);

        var presetMappings = tenant.InstitutionTypeDefinitionId.HasValue
            ? await _presetModuleRepository.GetQueryable()
                .AsNoTracking()
                .Where(x => x.InstitutionTypeDefinitionId == tenant.InstitutionTypeDefinitionId.Value)
                .ToDictionaryAsync(x => x.ProductModuleId)
            : new Dictionary<long, InstitutionTypeModule>();

        var entitledModuleIds = await GetEntitledModuleIdsAsync(tenantId, modules, now);
        var result = new List<TenantModuleDto>(modules.Count);

        foreach (var module in modules)
        {
            selections.TryGetValue(module.Id, out var selection);
            presetMappings.TryGetValue(module.Id, out var preset);

            var selected = module.IsCore
                           || (selection?.IsEnabled
                               ?? preset is { IsEnabledByDefault: true } or { IsRequired: true });
            var inEffectivePeriod = selection == null
                                    || (!selection.EffectiveFromUtc.HasValue || selection.EffectiveFromUtc <= now)
                                    && (!selection.EffectiveUntilUtc.HasValue || selection.EffectiveUntilUtc > now);
            var includedInPlan = module.IsCore || entitledModuleIds.Contains(module.Id);
            var isAvailable = selected && inEffectivePeriod && includedInPlan;

            result.Add(new TenantModuleDto
            {
                ProductModuleId = module.Id,
                Code = module.Code,
                Name = module.Name,
                NameBangla = module.NameBangla,
                Category = module.Category,
                IconName = module.IconName,
                RoutePrefix = module.RoutePrefix,
                IsCore = module.IsCore,
                IsRequiredForInstitution = module.IsCore || preset?.IsRequired == true,
                IsSelected = selected,
                IsIncludedInPlan = includedInPlan,
                IsAvailable = isAvailable,
                CanEnable = includedInPlan,
                CanDisable = !module.IsCore && preset?.IsRequired != true,
                ActivationSource = selection?.ActivationSource.ToString()
                                   ?? TenantModuleActivationSource.InstitutionPreset.ToString(),
                EffectiveFromUtc = selection?.EffectiveFromUtc,
                EffectiveUntilUtc = selection?.EffectiveUntilUtc,
                AvailabilityReasonCode = ResolveReasonCode(
                    selected,
                    inEffectivePeriod,
                    includedInPlan),
                ConfigurationVersion = selection?.ConfigurationVersion ?? 1,
                RowVersion = selection?.RowVersion.Length > 0
                    ? Convert.ToBase64String(selection.RowVersion)
                    : null
            });
        }

        return result;
    }

    private async Task<HashSet<long>> GetEntitledModuleIdsAsync(
        long tenantId,
        IReadOnlyCollection<ProductModule> modules,
        DateTime now)
    {
        var activePlanId = await _subscriptionRepository.GetQueryable()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && x.StartDate <= now
                        && x.EndDate >= now
                        && (x.Status == SubscriptionStatus.Active
                            || x.Status == SubscriptionStatus.Trialing
                            || x.Status == SubscriptionStatus.CancelAtPeriodEnd))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (long?)x.SubscriptionPlanId)
            .FirstOrDefaultAsync();

        if (!activePlanId.HasValue) return new HashSet<long>();

        var enabledFeatureIds = await _planFeatureRepository.GetQueryable()
            .AsNoTracking()
            .Where(x => x.SubscriptionPlanId == activePlanId.Value && x.IsEnabled)
            .Select(x => x.FeatureId)
            .ToListAsync();
        if (enabledFeatureIds.Count == 0) return new HashSet<long>();

        var moduleIds = modules.Select(x => x.Id).ToList();
        var entitledIds = await _moduleFeatureRepository.GetQueryable()
            .AsNoTracking()
            .Where(x => moduleIds.Contains(x.ProductModuleId)
                        && enabledFeatureIds.Contains(x.FeatureId))
            .Select(x => x.ProductModuleId)
            .Distinct()
            .ToListAsync();
        return entitledIds.ToHashSet();
    }

    private async Task<bool> IsModuleIncludedInCurrentPlanAsync(long tenantId, ProductModule module)
    {
        if (module.IsCore) return true;
        var entitled = await GetEntitledModuleIdsAsync(tenantId, new[] { module }, DateTime.UtcNow);
        return entitled.Contains(module.Id);
    }

    private static bool MatchesExpectedRowVersion(
        TenantModule tenantModule,
        string? encodedRowVersion)
    {
        if (string.IsNullOrWhiteSpace(encodedRowVersion)) return false;
        var expected = Convert.FromBase64String(encodedRowVersion);
        return expected.AsSpan().SequenceEqual(tenantModule.RowVersion);
    }

    private static string ResolveReasonCode(
        bool isSelected,
        bool inEffectivePeriod,
        bool includedInPlan)
    {
        if (!isSelected) return "MODULE_NOT_SELECTED";
        if (!inEffectivePeriod) return "OUTSIDE_EFFECTIVE_PERIOD";
        if (!includedInPlan) return "NOT_INCLUDED_IN_PLAN";
        return "AVAILABLE";
    }

    private static bool TryNormalizeCode(string? code, out string normalizedCode)
    {
        normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalizedCode.Length is > 0 and <= 50
               && normalizedCode.All(character =>
                   char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }
}
