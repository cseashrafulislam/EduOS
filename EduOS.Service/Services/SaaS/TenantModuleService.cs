using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

public sealed class TenantModuleService : ITenantModuleService
{
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<ProductModule> _modules;
    private readonly IGenericRepository<InstitutionTypeModule> _presets;
    private readonly IGenericRepository<TenantModule> _selections;
    private readonly IGenericRepository<ProductModuleFeature> _moduleFeatures;
    private readonly IGenericRepository<PlanFeature> _planFeatures;
    private readonly IGenericRepository<TenantSubscription> _subscriptions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<TenantModuleService> _logger;

    public TenantModuleService(IGenericRepository<Tenant> tenants, IGenericRepository<ProductModule> modules,
        IGenericRepository<InstitutionTypeModule> presets, IGenericRepository<TenantModule> selections,
        IGenericRepository<ProductModuleFeature> moduleFeatures, IGenericRepository<PlanFeature> planFeatures,
        IGenericRepository<TenantSubscription> subscriptions, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, ILogger<TenantModuleService> logger)
    {
        _tenants = tenants; _modules = modules; _presets = presets; _selections = selections;
        _moduleFeatures = moduleFeatures; _planFeatures = planFeatures;
        _subscriptions = subscriptions; _unitOfWork = unitOfWork;
        _currentUser = currentUser; _logger = logger;
    }

    public async Task<ApiResponse<List<TenantModuleDto>>> GetCurrentTenantModulesAsync()
    {
        if (!CanRead()) return ApiResponse<List<TenantModuleDto>>.ErrorResponse("Tenant context is required.", 403);
        try { return ApiResponse<List<TenantModuleDto>>.SuccessResponse(await BuildStateAsync(_currentUser.TenantId)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module selection read failed for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<List<TenantModuleDto>>.ErrorResponse("Tenant modules could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<TenantModuleDto>> UpdateCurrentTenantModuleAsync(string moduleCode, UpdateTenantModuleRequestDto request)
    {
        if (!CanManage()) return ApiResponse<TenantModuleDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request?.IsEnabled == null || !TryNormalizeCode(moduleCode, out var code))
            return ApiResponse<TenantModuleDto>.ErrorResponse("Valid module code and enabled state are required.");
        if (request.EffectiveFromUtc.HasValue || request.EffectiveUntilUtc.HasValue ||
            !string.IsNullOrWhiteSpace(request.DisabledReason))
            return ApiResponse<TenantModuleDto>.ErrorResponse("Scheduled activation and disable notes are not supported by the current module contract.");

        var tenantId = _currentUser.TenantId;
        try
        {
            var tenant = await _tenants.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tenantId);
            if (tenant == null) return ApiResponse<TenantModuleDto>.ErrorResponse("Institution not found.", 404);
            var module = await _modules.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.IsActive && x.Code == code);
            if (module == null) return ApiResponse<TenantModuleDto>.ErrorResponse("Module not found.", 404);
            var required = module.IsCore || (tenant.InstitutionTypeDefinitionId.HasValue &&
                await _presets.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.InstitutionTypeDefinitionId == tenant.InstitutionTypeDefinitionId.Value &&
                    x.ProductModuleId == module.Id && x.IsRequired));
            if (!request.IsEnabled.Value && required)
                return ApiResponse<TenantModuleDto>.ErrorResponse("Required modules cannot be disabled.", 409);
            if (request.IsEnabled.Value && !module.IsCore)
            {
                var entitled = await EntitledIdsAsync(tenantId);
                if (!entitled.Contains(module.Id))
                    return ApiResponse<TenantModuleDto>.ErrorResponse("Module is not included in the current subscription.", 403);
            }
            var now = DateTime.UtcNow;
            var record = await _selections.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                x.ProductModuleId == module.Id);
            if (record == null)
            {
                record = new TenantModule
                {
                    TenantId = tenantId, ProductModuleId = module.Id, IsEnabled = request.IsEnabled.Value,
                    CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                if (record.IsEnabled) record.EnabledAt = now;
                else record.DisabledAt = now;
                await _selections.AddAsync(record);
            }
            else
            {
                if (!MatchesVersion(record.RowVersion, request.RowVersion))
                    return ApiResponse<TenantModuleDto>.ErrorResponse("Module configuration changed. Reload and retry.", 409);
                record.IsEnabled = request.IsEnabled.Value;
                if (record.IsEnabled) { record.EnabledAt = now; record.DisabledAt = null; }
                else { record.DisabledAt = now; }
                record.UpdatedAt = now;
                record.UpdatedBy = _currentUser.UserId;
            }
            await _unitOfWork.SaveChangesAsync();
            var updated = (await BuildStateAsync(tenantId)).Single(x => x.ModuleCode == code);
            return ApiResponse<TenantModuleDto>.SuccessResponse(updated, "Module selection saved.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TenantModuleDto>.ErrorResponse("Module was modified. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent module change for tenant {TenantId}", tenantId);
            return ApiResponse<TenantModuleDto>.ErrorResponse("Module selection conflicts with another update.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module selection failed for tenant {TenantId}", tenantId);
            return ApiResponse<TenantModuleDto>.ErrorResponse("Module selection could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<bool>> ValidateCurrentTenantSelectionAsync()
    {
        if (!CanRead()) return ApiResponse<bool>.ErrorResponse("Tenant context is required.", 403);
        try
        {
            var tenant = await _tenants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.Id == _currentUser.TenantId);
            if (tenant == null) return ApiResponse<bool>.ErrorResponse("Institution not found.", 404);
            var modules = await BuildStateAsync(_currentUser.TenantId);
            if (modules.Count == 0) return ApiResponse<bool>.ErrorResponse("No enabled product modules exist.", 409);
            if (modules.Any(x => x.IsRequiredByPreset && (!x.IsEnabled || !x.IsEntitledByPlan)))
                return ApiResponse<bool>.ErrorResponse("Required institution modules must be enabled and included in the subscription.", 409);
            return ApiResponse<bool>.SuccessResponse(true, "Module selection is valid.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module selection validation failed for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<bool>.ErrorResponse("Module selection could not be validated.", 500);
        }
    }

    public async Task<bool> IsCurrentTenantModuleAvailableAsync(string moduleCode)
    {
        if (!CanRead() || !TryNormalizeCode(moduleCode, out var code)) return false;
        try
        {
            var rows = await BuildStateAsync(_currentUser.TenantId);
            return rows.Any(x => x.ModuleCode == code && x.IsEnabled && x.IsEntitledByPlan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module authorization evaluation failed for tenant {TenantId}", _currentUser.TenantId);
            return false;
        }
    }

    public async Task<ApiResponse<bool>> ApplyInstitutionPresetAsync(long tenantId, long institutionTypeDefinitionId, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || !(_currentUser.IsSuperAdmin ||
            (_currentUser.IsTenantAdmin && _currentUser.TenantId == tenantId)))
            return ApiResponse<bool>.ErrorResponse("Not authorized to configure modules for this institution.");
        if (tenantId <= 0 || institutionTypeDefinitionId <= 0)
            return ApiResponse<bool>.ErrorResponse("Valid institution type and tenant are required.");
        try
        {
            var tenant = await _tenants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId);
            if (tenant == null || tenant.InstitutionTypeDefinitionId != institutionTypeDefinitionId)
                return ApiResponse<bool>.ErrorResponse("Institution type is not linked to the requested tenant.");
            var preset = await _presets.GetQueryable().AsNoTracking().Where(x =>
                x.InstitutionTypeDefinitionId == institutionTypeDefinitionId &&
                (x.IsRequired || x.IsDefaultEnabled)).ToListAsync(cancellationToken);
            var existing = await _selections.GetQueryable().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
            var byModule = existing.ToDictionary(x => x.ProductModuleId);
            var now = DateTime.UtcNow;
            foreach (var item in preset)
            {
                if (byModule.TryGetValue(item.ProductModuleId, out var selected))
                {
                    if (item.IsRequired && !selected.IsEnabled)
                    {
                        selected.IsEnabled = true;
                        selected.EnabledAt = now;
                        selected.DisabledAt = null;
                        selected.UpdatedAt = now;
                        selected.UpdatedBy = _currentUser.UserId;
                    }
                    continue;
                }
                await _selections.AddAsync(new TenantModule
                {
                    TenantId = tenantId, ProductModuleId = item.ProductModuleId, IsEnabled = true,
                    EnabledAt = now, CreatedAt = now, CreatedBy = _currentUser.UserId
                });
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Preset conflict for tenant {TenantId}", tenantId);
            return ApiResponse<bool>.ErrorResponse("Institution preset was updated concurrently.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Institution preset failed for tenant {TenantId}", tenantId);
            return ApiResponse<bool>.ErrorResponse("Institution preset could not be applied.");
        }
    }

    private async Task<List<TenantModuleDto>> BuildStateAsync(long tenantId)
    {
        var tenant = await _tenants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId)
            ?? throw new InvalidOperationException("Institution not found.");
        var modules = await _modules.GetQueryable().AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync();
        var selections = await _selections.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.ProductModuleId);
        var required = tenant.InstitutionTypeDefinitionId.HasValue
            ? await _presets.GetQueryable().AsNoTracking().Where(x =>
                x.InstitutionTypeDefinitionId == tenant.InstitutionTypeDefinitionId.Value)
                .ToDictionaryAsync(x => x.ProductModuleId)
            : new Dictionary<long, InstitutionTypeModule>();
        var entitled = await EntitledIdsAsync(tenantId);
        return modules.Select(module =>
        {
            selections.TryGetValue(module.Id, out var selected);
            required.TryGetValue(module.Id, out var preset);
            var isRequired = module.IsCore || preset?.IsRequired == true;
            var isEnabled = module.IsCore || (selected?.IsEnabled ?? (preset?.IsDefaultEnabled == true || isRequired));
            return new TenantModuleDto
            {
                Id = selected?.Id ?? 0, ProductModuleId = module.Id,
                ModuleCode = module.Code, ModuleName = module.Name,
                IsEnabled = isEnabled, IsRequiredByPreset = isRequired,
                IsEntitledByPlan = module.IsCore || entitled.Contains(module.Id),
                EnabledAt = selected?.EnabledAt, DisabledAt = selected?.DisabledAt,
                RowVersion = selected == null ? string.Empty : Convert.ToBase64String(selected.RowVersion)
            };
        }).ToList();
    }

    private async Task<HashSet<long>> EntitledIdsAsync(long tenantId)
    {
        var now = DateTime.UtcNow;
        var planId = await _subscriptions.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StartsAt <= now && x.EndsAt > now &&
            (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial || x.State == SubscriptionState.Grace))
            .OrderByDescending(x => x.StartsAt).Select(x => (long?)x.SubscriptionPlanId).FirstOrDefaultAsync();
        if (!planId.HasValue) return new HashSet<long>();
        var features = await _planFeatures.GetQueryable().AsNoTracking().Where(x =>
            x.SubscriptionPlanId == planId.Value && x.IsEnabled).Select(x => x.FeatureId).ToArrayAsync();
        var result = await _moduleFeatures.GetQueryable().AsNoTracking().Where(x =>
            features.Contains(x.FeatureId)).Select(x => x.ProductModuleId).Distinct().ToArrayAsync();
        return result.ToHashSet();
    }

    private static bool MatchesVersion(byte[] stored, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return stored.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static bool TryNormalizeCode(string? value, out string code)
    {
        code = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return code.Length is > 0 and <= 100 &&
            code.All(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-');
    }
    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0;
    private bool CanManage() => CanRead() && (_currentUser.IsTenantAdmin || _currentUser.IsSuperAdmin);
}
