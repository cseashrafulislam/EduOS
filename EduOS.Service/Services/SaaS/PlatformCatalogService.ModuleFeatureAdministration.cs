using System.Data;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

// Product-module/feature assignments are host catalog configuration, not plan
// entitlements or tenant overrides. The existing PlatformCatalogService owns them.
public partial class PlatformCatalogService
{
    private static string ModuleFeatureSnapshot(long moduleId, long featureId, bool mapped) =>
        JsonSerializer.Serialize(new { ProductModuleId = moduleId, FeatureId = featureId, IsMapped = mapped });

    private static PlatformModuleFeatureRowDto MapModuleFeature(Feature feature, ProductModuleFeature? link) => new()
    {
        FeatureId = feature.Id,
        FeatureCode = feature.Code,
        FeatureName = feature.Name,
        IsActive = feature.IsActive,
        IsMapped = link is { IsDeleted: false },
        MappingId = link?.Id,
        FeatureRowVersion = Convert.ToBase64String(feature.RowVersion),
        MappingRowVersion = link == null ? null : Convert.ToBase64String(link.RowVersion)
    };

    public async Task<ApiResponse<PlatformModuleFeatureDirectoryDto>> GetAdminModuleFeaturesAsync(
        long moduleId, CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<PlatformModuleFeatureDirectoryDto>.ErrorResponse("Platform administrator required.", 403);
        var db = _adminDb!;
        var module = await db.ProductModules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == moduleId && !x.IsDeleted, ct);
        if (module == null)
            return ApiResponse<PlatformModuleFeatureDirectoryDto>.ErrorResponse("Module not found.", 404);
        var features = await db.Features.AsNoTracking().Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code).ToListAsync(ct);
        // Include historical soft-deleted mappings so an administrator can
        // re-enable the same canonical link without creating a duplicate.
        var links = await db.ProductModuleFeatures.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ProductModuleId == moduleId)
            .OrderByDescending(x => x.Id).ToListAsync(ct);
        var byFeature = links.GroupBy(x => x.FeatureId)
            .ToDictionary(x => x.Key, x => x.FirstOrDefault(y => !y.IsDeleted) ?? x.First());
        return ApiResponse<PlatformModuleFeatureDirectoryDto>.SuccessResponse(new PlatformModuleFeatureDirectoryDto
        {
            ModuleId = module.Id, ModuleCode = module.Code, ModuleName = module.Name,
            IsCore = module.IsCore, IsActive = module.IsActive,
            ModuleRowVersion = Convert.ToBase64String(module.RowVersion),
            Features = features.Select(x => MapModuleFeature(x, byFeature.GetValueOrDefault(x.Id))).ToList()
        });
    }

    public async Task<ApiResponse<PlatformModuleFeatureRowDto>> SaveModuleFeatureMappingAsync(
        long moduleId, long featureId, SavePlatformModuleFeatureRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse("Platform administrator required.", 403);
        if (moduleId <= 0 || featureId <= 0 || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ModuleRowVersion) ||
            string.IsNullOrWhiteSpace(request.FeatureRowVersion))
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse(
                "Module, feature, request ID and catalog versions are required.", 400);

        static bool Decode(string? value, out byte[] bytes)
        {
            try { bytes = Convert.FromBase64String(value ?? ""); return bytes.Length > 0; }
            catch (FormatException) { bytes = Array.Empty<byte>(); return false; }
        }
        if (!Decode(request.ModuleRowVersion, out var moduleVersion) ||
            !Decode(request.FeatureRowVersion, out var featureVersion) ||
            (!string.IsNullOrWhiteSpace(request.MappingRowVersion) &&
             !Decode(request.MappingRowVersion, out _)))
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse("Invalid row version.", 400);

        var db = _adminDb!;
        var user = _adminUser!;
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var module = await db.ProductModules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == moduleId && !x.IsDeleted, ct);
        var feature = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == featureId && !x.IsDeleted, ct);
        if (module == null || feature == null)
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse("Catalog entry not found.", 404);

        var candidates = await db.ProductModuleFeatures.IgnoreQueryFilters()
            .Where(x => x.ProductModuleId == moduleId && x.FeatureId == featureId)
            .OrderByDescending(x => x.Id).ToListAsync(ct);
        var link = candidates.FirstOrDefault(x => !x.IsDeleted) ?? candidates.FirstOrDefault();
        var current = ModuleFeatureSnapshot(moduleId, featureId, link is { IsDeleted: false });
        var desired = ModuleFeatureSnapshot(moduleId, featureId, request.IsMapped);
        var requestId = request.RequestId.ToString("D");
        // Host-only audit deduplication; never expose tenant data.
        var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == null && !x.IsDeleted &&
            x.Action == "ProductModuleFeature.MappingChanged" && x.RequestId == requestId, ct);
        if (prior != null)
            return prior.UserId == user.UserId && prior.NewValue == desired && current == desired
                ? ApiResponse<PlatformModuleFeatureRowDto>.SuccessResponse(MapModuleFeature(feature, link), "Already applied.")
                : ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse("Request ID already used or mapping changed.", 409);

        if (!module.RowVersion.AsSpan().SequenceEqual(moduleVersion) ||
            !feature.RowVersion.AsSpan().SequenceEqual(featureVersion))
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse("Catalog changed. Refresh and retry.", 409);
        if (link == null && !string.IsNullOrWhiteSpace(request.MappingRowVersion))
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse("Mapping changed. Refresh and retry.", 409);
        if (link != null && (!Decode(request.MappingRowVersion, out var mappingVersion) ||
            !link.RowVersion.AsSpan().SequenceEqual(mappingVersion)))
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse("Mapping changed. Refresh and retry.", 409);

        if (request.IsMapped && (!module.IsActive || !feature.IsActive))
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse(
                "Inactive modules or features cannot receive mappings.", 409);
        if (current == desired)
            return ApiResponse<PlatformModuleFeatureRowDto>.SuccessResponse(
                MapModuleFeature(feature, link), "Already in requested state.");

        if (!request.IsMapped)
        {
            if (module.IsCore)
                return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse(
                    "Core module feature mappings cannot be removed.", 409);
            // Removing a mapping may revoke module access granted by a plan.
            // Require tenant reconciliation rather than changing existing access silently.
            var selected = await db.TenantModules.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                !x.IsDeleted && x.TenantId > 0 && x.ProductModuleId == moduleId && x.IsEnabled, ct);
            var implicitSelection = await (
                from tenant in db.Tenants.IgnoreQueryFilters().AsNoTracking()
                join preset in db.InstitutionTypeModules.IgnoreQueryFilters().AsNoTracking()
                    on tenant.InstitutionTypeDefinitionId equals (long?)preset.InstitutionTypeDefinitionId
                where !tenant.IsDeleted && !preset.IsDeleted && preset.ProductModuleId == moduleId &&
                      (preset.IsRequired || preset.IsDefaultEnabled)
                select tenant.Id).AnyAsync(ct);
            if (selected || implicitSelection)
                return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse(
                    "Reconcile existing institution module access before removing this feature mapping.", 409);
        }

        var now = DateTime.UtcNow;
        if (link == null)
        {
            link = new ProductModuleFeature
            {
                ProductModuleId = moduleId, FeatureId = featureId,
                CreatedAt = now, CreatedBy = user.UserId
            };
            if (!db.Database.IsRelational()) link.RowVersion = Guid.NewGuid().ToByteArray();
            db.ProductModuleFeatures.Add(link);
        }
        else
        {
            link.IsDeleted = !request.IsMapped;
            link.DeletedAt = request.IsMapped ? null : now;
            link.DeletedBy = request.IsMapped ? null : user.UserId;
            link.UpdatedAt = now;
            link.UpdatedBy = user.UserId;
            if (!db.Database.IsRelational()) link.RowVersion = Guid.NewGuid().ToByteArray();
        }
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = null, UserId = user.UserId,
            Action = "ProductModuleFeature.MappingChanged",
            EntityName = nameof(ProductModule), EntityId = moduleId,
            OldValue = current, NewValue = desired, RequestId = requestId,
            Endpoint = "/api/platform/catalog/module-features",
            IsSuccess = true, OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
        });
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return ApiResponse<PlatformModuleFeatureRowDto>.SuccessResponse(
                MapModuleFeature(feature, link), "Module feature mapping updated.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse("Mapping changed. Refresh and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Module feature mapping conflict {ModuleId}/{FeatureId}", moduleId, featureId);
            return ApiResponse<PlatformModuleFeatureRowDto>.ErrorResponse(
                "Mapping update conflicted with another operation.", 409);
        }
    }
}
