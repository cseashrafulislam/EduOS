using System.Data;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

// Host catalog presets affect future onboarding; existing tenant module selections are never rewritten.
public partial class PlatformCatalogService
{
    private static PlatformInstitutionModuleRowDto PresetRow(ProductModule module, InstitutionTypeModule? preset) => new()
    {
        ModuleId = module.Id, ModuleCode = module.Code, ModuleName = module.Name,
        IsCore = module.IsCore, IsActive = module.IsActive,
        IsRequired = preset?.IsRequired ?? false,
        IsDefaultEnabled = preset?.IsDefaultEnabled ?? false,
        PresetId = preset?.Id, RowVersion = preset == null ? null : Convert.ToBase64String(preset.RowVersion)
    };

    private static string PresetSnapshot(long typeId, long moduleId, bool required, bool enabled) =>
        JsonSerializer.Serialize(new { InstitutionTypeId = typeId, ProductModuleId = moduleId,
            IsRequired = required, IsDefaultEnabled = enabled });

    public async Task<ApiResponse<PlatformInstitutionModulePresetDto>> GetAdminInstitutionModulePresetAsync(
        long institutionTypeId, CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<PlatformInstitutionModulePresetDto>.ErrorResponse("Platform administrator required.", 403);
        var db = _adminDb!;
        var type = await db.InstitutionTypeDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == institutionTypeId && !x.IsDeleted, ct);
        if (type == null)
            return ApiResponse<PlatformInstitutionModulePresetDto>.ErrorResponse("Institution type not found.", 404);
        var modules = await db.ProductModules.AsNoTracking().Where(x => !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code).ToListAsync(ct);
        var presets = await db.InstitutionTypeModules.AsNoTracking()
            .Where(x => x.InstitutionTypeDefinitionId == institutionTypeId && !x.IsDeleted)
            .ToDictionaryAsync(x => x.ProductModuleId, ct);
        return ApiResponse<PlatformInstitutionModulePresetDto>.SuccessResponse(new PlatformInstitutionModulePresetDto
        {
            InstitutionTypeId = type.Id, InstitutionTypeCode = type.Code, InstitutionTypeName = type.Name,
            InstitutionTypeRowVersion = Convert.ToBase64String(type.RowVersion),
            Modules = modules.Select(m => PresetRow(m, presets.GetValueOrDefault(m.Id))).ToList()
        });
    }

    public async Task<ApiResponse<PlatformInstitutionModuleRowDto>> SaveInstitutionModulePresetAsync(
        long institutionTypeId, long moduleId, SavePlatformInstitutionModulePresetDto request,
        CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Platform administrator required.", 403);
        if (institutionTypeId <= 0 || moduleId <= 0 || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.InstitutionTypeRowVersion))
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse(
                "Institution, module, request ID and catalog version are required.", 400);
        if (request.IsRequired && !request.IsDefaultEnabled)
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse(
                "A required module must be enabled by default.", 400);

        byte[] typeVersion;
        try { typeVersion = Convert.FromBase64String(request.InstitutionTypeRowVersion); }
        catch (FormatException)
        { return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Invalid catalog version.", 400); }
        if (typeVersion.Length == 0)
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Invalid catalog version.", 400);

        var db = _adminDb!;
        var user = _adminUser!;
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var type = await db.InstitutionTypeDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == institutionTypeId && !x.IsDeleted, ct);
        var module = await db.ProductModules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == moduleId && !x.IsDeleted, ct);
        if (type == null || module == null)
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Catalog entry not found.", 404);
        if (!type.RowVersion.AsSpan().SequenceEqual(typeVersion))
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Institution catalog changed. Refresh and retry.", 409);
        if (module.IsCore && (!request.IsRequired || !request.IsDefaultEnabled))
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Core modules must be required and enabled.", 409);
        if (!module.IsActive && (request.IsRequired || request.IsDefaultEnabled))
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Inactive modules cannot be enabled in a preset.", 409);

        var row = await db.InstitutionTypeModules.FirstOrDefaultAsync(x =>
            x.InstitutionTypeDefinitionId == institutionTypeId && x.ProductModuleId == moduleId && !x.IsDeleted, ct);
        var desired = PresetSnapshot(institutionTypeId, moduleId, request.IsRequired, request.IsDefaultEnabled);
        var current = PresetSnapshot(institutionTypeId, moduleId, row?.IsRequired ?? false, row?.IsDefaultEnabled ?? false);
        var requestId = request.RequestId.ToString("D");
        // Explicit host-only audit lookup. Tenant records remain behind their normal filters.
        var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == null && !x.IsDeleted && x.Action == "InstitutionTypeModule.PresetChanged" &&
            x.RequestId == requestId, ct);
        if (prior != null)
            return prior.UserId == user.UserId && prior.NewValue == desired && current == desired
                ? ApiResponse<PlatformInstitutionModuleRowDto>.SuccessResponse(PresetRow(module, row), "Already applied.")
                : ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Request ID already used or preset changed.", 409);

        if (row != null)
        {
            byte[] supplied;
            try { supplied = Convert.FromBase64String(request.RowVersion ?? ""); }
            catch (FormatException)
            { return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Invalid preset version.", 400); }
            if (supplied.Length == 0 || !row.RowVersion.AsSpan().SequenceEqual(supplied))
                return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Preset changed. Refresh and retry.", 409);
        }
        else if (!string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Preset was added. Refresh and retry.", 409);

        // A new required module could invalidate existing institutions whose plan or selections exclude it.
        // Do not impose that policy retroactively: require migration/coordination of tenants first.
        if (request.IsRequired && row?.IsRequired != true &&
            await db.Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                !x.IsDeleted && x.InstitutionTypeDefinitionId == institutionTypeId, ct))
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse(
                "Existing institutions must be reconciled before making this module required.", 409);

        if (current == desired)
            return ApiResponse<PlatformInstitutionModuleRowDto>.SuccessResponse(
                PresetRow(module, row), "Already in requested state.");

        var now = DateTime.UtcNow;
        if (row == null)
        {
            row = new InstitutionTypeModule
            {
                InstitutionTypeDefinitionId = institutionTypeId, ProductModuleId = moduleId,
                IsRequired = request.IsRequired, IsDefaultEnabled = request.IsDefaultEnabled,
                CreatedAt = now, CreatedBy = user.UserId
            };
            // SQL Server generates rowversion; in-memory provider needs a token for regression tests.
            if (!db.Database.IsRelational()) row.RowVersion = Guid.NewGuid().ToByteArray();
            db.InstitutionTypeModules.Add(row);
        }
        else
        {
            row.IsRequired = request.IsRequired;
            row.IsDefaultEnabled = request.IsDefaultEnabled;
            row.UpdatedAt = now;
            row.UpdatedBy = user.UserId;
        }
        var audit = new AuditLog
        {
            TenantId = null, UserId = user.UserId, Action = "InstitutionTypeModule.PresetChanged",
            EntityName = nameof(InstitutionTypeModule), EntityId = row.Id,
            OldValue = current, NewValue = desired, RequestId = requestId,
            Endpoint = "/api/platform/catalog/institution-module-presets",
            IsSuccess = true, OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
        };
        db.AuditLogs.Add(audit);
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return ApiResponse<PlatformInstitutionModuleRowDto>.SuccessResponse(
                PresetRow(module, row), "Institution module preset updated.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Preset changed. Refresh and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Institution module preset conflict for {TypeId}/{ModuleId}", institutionTypeId, moduleId);
            return ApiResponse<PlatformInstitutionModuleRowDto>.ErrorResponse("Preset update conflicted with another operation.", 409);
        }
    }
}
