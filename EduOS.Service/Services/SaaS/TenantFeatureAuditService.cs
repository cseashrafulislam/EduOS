using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

/// <summary>Read-only feature entitlement and stored-override audit. Does not change access policies.</summary>
public sealed class TenantFeatureAuditService(
    EduOSDbContext db, ICurrentUserService user, ILogger<TenantFeatureAuditService> logger) : ITenantFeatureAuditService
{
    public async Task<ApiResponse<PagedResult<TenantFeatureAuditRowDto>>> GetAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 || !user.IsTenantAdmin)
            return ApiResponse<PagedResult<TenantFeatureAuditRowDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<TenantFeatureAuditRowDto>>.ErrorResponse("Invalid paging.");
        try
        {
            var now = DateTime.UtcNow;
            var planId = await db.TenantSubscriptions.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && x.StartsAt <= now && x.EndsAt > now &&
                    (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial || x.State == SubscriptionState.Grace))
                .OrderByDescending(x => x.StartsAt)
                .Select(x => (long?)x.SubscriptionPlanId).FirstOrDefaultAsync(cancellationToken);
            var query = db.Features.AsNoTracking().Where(x => x.IsActive);
            var count = await query.CountAsync(cancellationToken);
            var features = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            var ids = features.Select(x => x.Id).ToArray();
            var planIds = planId.HasValue
                ? await db.PlanFeatures.AsNoTracking().Where(x =>
                    x.SubscriptionPlanId == planId.Value && x.IsEnabled && ids.Contains(x.FeatureId))
                    .Select(x => x.FeatureId).ToArrayAsync(cancellationToken)
                : Array.Empty<long>();
            var overrides = await db.TenantFeatures.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && ids.Contains(x.FeatureId))
                .ToDictionaryAsync(x => x.FeatureId, cancellationToken);
            var entitled = planIds.ToHashSet();
            var rows = features.Select(x =>
            {
                overrides.TryGetValue(x.Id, out var o);
                var active = o != null && (!o.EffectiveFrom.HasValue || o.EffectiveFrom <= now) &&
                    (!o.EffectiveTo.HasValue || o.EffectiveTo > now);
                return new TenantFeatureAuditRowDto
                {
                    FeatureId = x.Id, FeatureCode = x.Code, FeatureName = x.Name,
                    IncludedInPlan = entitled.Contains(x.Id), HasOverride = o != null,
                    OverrideCurrentlyEffective = active,
                    RestrictedInConfiguration = active && !o!.IsEnabled,
                    EffectiveFrom = o?.EffectiveFrom, EffectiveTo = o?.EffectiveTo,
                    Reason = o?.Reason
                };
            }).ToList();
            return ApiResponse<PagedResult<TenantFeatureAuditRowDto>>.SuccessResponse(new()
            {
                Items = rows, TotalCount = count, Page = page, PageSize = pageSize
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Tenant feature audit failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<PagedResult<TenantFeatureAuditRowDto>>.ErrorResponse("Feature audit could not be loaded.", 500);
        }
    }
}
