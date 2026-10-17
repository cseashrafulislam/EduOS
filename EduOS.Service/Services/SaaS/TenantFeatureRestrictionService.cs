using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

/// <summary>Tenant administrator deny-only feature overrides; the plan remains the entitlement ceiling.</summary>
public sealed class TenantFeatureRestrictionService(
    EduOSDbContext db, ICurrentUserService user, ILogger<TenantFeatureRestrictionService> logger)
    : ITenantFeatureRestrictionService
{
    private bool CanManage => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0;
    private static bool VersionMatches(byte[] stored, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return stored.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static TenantFeatureRestrictionDto Map(Feature feature, bool included, TenantFeature? row, DateTime now) => new()
    {
        FeatureId = feature.Id, FeatureCode = feature.Code, FeatureName = feature.Name,
        IncludedInPlan = included, RestrictionConfigured = row != null && !row.IsEnabled,
        RestrictedNow = row != null && !row.IsEnabled &&
            (!row.EffectiveFrom.HasValue || row.EffectiveFrom <= now) &&
            (!row.EffectiveTo.HasValue || row.EffectiveTo > now),
        EffectiveFrom = row?.EffectiveFrom, EffectiveTo = row?.EffectiveTo,
        Reason = row?.Reason, RowVersion = row == null ? string.Empty : Convert.ToBase64String(row.RowVersion)
    };
    private async Task<long?> CurrentPlanAsync(DateTime now, CancellationToken ct) =>
        await db.TenantSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.StartsAt <= now && x.EndsAt > now &&
                (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial || x.State == SubscriptionState.Grace))
            .OrderByDescending(x => x.StartsAt).Select(x => (long?)x.SubscriptionPlanId)
            .FirstOrDefaultAsync(ct);

    public async Task<ApiResponse<PagedResult<TenantFeatureRestrictionDto>>> GetAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<TenantFeatureRestrictionDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<TenantFeatureRestrictionDto>>.ErrorResponse("Invalid paging.");
        try
        {
            var now = DateTime.UtcNow;
            var planId = await CurrentPlanAsync(now, cancellationToken);
            var query = db.Features.AsNoTracking().Where(x => x.IsActive);
            var count = await query.CountAsync(cancellationToken);
            var features = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            var ids = features.Select(x => x.Id).ToArray();
            var entitled = planId.HasValue
                ? (await db.PlanFeatures.AsNoTracking().Where(x => x.SubscriptionPlanId == planId.Value &&
                    x.IsEnabled && ids.Contains(x.FeatureId)).Select(x => x.FeatureId).ToArrayAsync(cancellationToken)).ToHashSet()
                : new HashSet<long>();
            var overrides = await db.TenantFeatures.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && ids.Contains(x.FeatureId))
                .ToDictionaryAsync(x => x.FeatureId, cancellationToken);
            var rows = features.Select(x =>
            {
                overrides.TryGetValue(x.Id, out var record);
                return Map(x, entitled.Contains(x.Id), record, now);
            }).ToList();
            return ApiResponse<PagedResult<TenantFeatureRestrictionDto>>.SuccessResponse(new()
            {
                Items = rows, TotalCount = count, Page = page, PageSize = pageSize
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Feature restriction list failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<PagedResult<TenantFeatureRestrictionDto>>.ErrorResponse("Feature restrictions could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<TenantFeatureRestrictionDto>> SaveAsync(
        long featureId, SaveTenantFeatureRestrictionRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Tenant administrator required.", 403);
        if (featureId <= 0 || request == null || request.Reason?.Length > 500 ||
            request.Restricted && string.IsNullOrWhiteSpace(request.Reason) ||
            request.EffectiveFrom.HasValue && request.EffectiveTo.HasValue &&
                request.EffectiveFrom.Value >= request.EffectiveTo.Value)
            return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Invalid feature restriction.");
        var now = DateTime.UtcNow;
        var from = request.EffectiveFrom?.UtcDateTime;
        var to = request.EffectiveTo?.UtcDateTime;
        if (request.Restricted && to.HasValue && to <= now)
            return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Restriction end must be in the future.");
        try
        {
            var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == featureId && x.IsActive, cancellationToken);
            if (feature == null) return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Feature not found.", 404);
            if (request.Restricted)
            {
                var tenantType = await db.Tenants.AsNoTracking().Where(x => x.Id == user.TenantId)
                    .Select(x => x.InstitutionTypeDefinitionId).FirstOrDefaultAsync(cancellationToken);
                var protectedFeature = await
                    (from mf in db.ProductModuleFeatures.AsNoTracking()
                     join m in db.ProductModules.AsNoTracking() on mf.ProductModuleId equals m.Id
                     where mf.FeatureId == featureId &&
                         (m.IsCore || (tenantType.HasValue && db.InstitutionTypeModules.Any(p =>
                             p.ProductModuleId == m.Id && p.InstitutionTypeDefinitionId == tenantType.Value && p.IsRequired)))
                     select mf.Id).AnyAsync(cancellationToken);
                if (protectedFeature)
                    return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Core or required institution features cannot be restricted.", 409);
            }
            var record = await db.TenantFeatures.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.FeatureId == featureId, cancellationToken);
            if (record == null)
            {
                if (!request.Restricted)
                    return ApiResponse<TenantFeatureRestrictionDto>.SuccessResponse(Map(feature, false, null, now),
                        "No restriction exists.");
                record = new TenantFeature
                {
                    TenantId = user.TenantId, FeatureId = featureId, CreatedBy = user.UserId
                };
                db.TenantFeatures.Add(record);
            }
            else if (!VersionMatches(record.RowVersion, request.RowVersion))
                return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Feature restriction changed; reload before saving.", 409);
            record.IsEnabled = !request.Restricted;
            record.EffectiveFrom = request.Restricted ? from : null;
            record.EffectiveTo = request.Restricted ? to : null;
            record.Reason = request.Restricted ? request.Reason!.Trim() : null;
            record.UpdatedAt = now;
            record.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(cancellationToken);
            var planId = await CurrentPlanAsync(now, cancellationToken);
            var included = planId.HasValue && await db.PlanFeatures.AsNoTracking().AnyAsync(x =>
                x.SubscriptionPlanId == planId.Value && x.FeatureId == featureId && x.IsEnabled, cancellationToken);
            return ApiResponse<TenantFeatureRestrictionDto>.SuccessResponse(Map(feature, included, record, now),
                "Feature restriction saved.");
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Feature restriction changed; reload before saving.", 409); }
        catch (DbUpdateException)
        { return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Feature restriction conflicts with another update.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Feature restriction save failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<TenantFeatureRestrictionDto>.ErrorResponse("Feature restriction could not be saved.", 500);
        }
    }
}
