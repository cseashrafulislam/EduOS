using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

// Host-owned product catalog metadata. Tenant selections and institution presets
// remain with their existing owners; module codes and core flags are immutable.
public partial class PlatformCatalogService
{
    private static PlatformProductModuleAdminDto MapProductModule(ProductModule row) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name,
        Description = row.Description, IsCore = row.IsCore,
        IsActive = row.IsActive, DisplayOrder = row.DisplayOrder,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static string ProductModuleSnapshot(string name, string? description, bool active, int order) =>
        JsonSerializer.Serialize(new { Name = name, Description = description, IsActive = active, DisplayOrder = order });

    public async Task<ApiResponse<IReadOnlyList<PlatformProductModuleAdminDto>>> GetAdminProductModulesAsync(
        CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<IReadOnlyList<PlatformProductModuleAdminDto>>.ErrorResponse(
                "Platform administrator required.", 403);
        var rows = await _adminDb!.ProductModules.AsNoTracking()
            .Where(x => !x.IsDeleted).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<PlatformProductModuleAdminDto>>.SuccessResponse(
            rows.Select(MapProductModule).ToList());
    }

    public async Task<ApiResponse<PlatformProductModuleAdminDto>> SaveProductModuleConfigurationAsync(
        long id, SavePlatformProductModuleRequestDto request, CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse("Platform administrator required.", 403);
        if (id <= 0 || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) || string.IsNullOrWhiteSpace(request.Name))
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                "Module, request ID, row version and name are required.", 400);

        var name = request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (name.Length > 150 || description?.Length > 500 ||
            request.DisplayOrder is < 0 or > 100000)
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                "Module metadata exceeds the permitted limits.", 400);

        byte[] supplied;
        try { supplied = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException)
        { return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse("Invalid row version.", 400); }
        if (supplied.Length == 0)
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse("Invalid row version.", 400);

        var db = _adminDb!;
        var user = _adminUser!;
        // Serialize the host catalog update with preset/tenant selection checks on
        // relational databases; in-memory unit tests do not support transactions.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;
        var row = await db.ProductModules.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (row == null)
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse("Module not found.", 404);

        var desired = ProductModuleSnapshot(name, description, request.IsActive, request.DisplayOrder);
        var current = ProductModuleSnapshot(row.Name, row.Description, row.IsActive, row.DisplayOrder);
        var requestId = request.RequestId.ToString("D");
        // Explicitly host-scoped audit deduplication; no tenant entity is modified.
        var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == null && !x.IsDeleted && x.EntityName == nameof(ProductModule) &&
            x.EntityId == id && x.Action == "ProductModule.ConfigurationChanged" &&
            x.RequestId == requestId, ct);
        if (prior != null)
            return prior.UserId == user.UserId && prior.NewValue == desired && current == desired
                ? ApiResponse<PlatformProductModuleAdminDto>.SuccessResponse(MapProductModule(row), "Already applied.")
                : ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                    "Request ID was already used or the module changed.", 409);

        if (!row.RowVersion.AsSpan().SequenceEqual(supplied))
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                "Module changed. Refresh and retry.", 409);

        if (row.IsCore && !request.IsActive)
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                "Core modules must remain active.", 409);
        if (!request.IsActive && row.IsActive)
        {
            // A preset can implicitly enable a module for a tenant without a
            // TenantModule row. Both presets and explicit selections must be cleared first.
            if (await db.InstitutionTypeModules.AsNoTracking().AnyAsync(x =>
                    x.ProductModuleId == id && (x.IsRequired || x.IsDefaultEnabled), ct))
                return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                    "Module is required or default-enabled by an institution preset.", 409);
            // Explicit SuperAdmin-only, module-scoped cross-tenant safety check.
            // Tenant query filters would otherwise hide every tenant except the
            // request tenant (zero for host administration). Return no tenant data.
            if (await db.TenantModules.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.ProductModuleId == id && x.TenantId > 0 && !x.IsDeleted && x.IsEnabled, ct))
                return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                    "Module is still enabled for an institution.", 409);
        }
        if (current == desired)
            return ApiResponse<PlatformProductModuleAdminDto>.SuccessResponse(
                MapProductModule(row), "Already in requested state.");

        var now = DateTime.UtcNow;
        row.Name = name;
        row.Description = description;
        row.IsActive = request.IsActive;
        row.DisplayOrder = request.DisplayOrder;
        row.UpdatedAt = now;
        row.UpdatedBy = user.UserId;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = null, UserId = user.UserId, Action = "ProductModule.ConfigurationChanged",
            EntityName = nameof(ProductModule), EntityId = id,
            OldValue = current, NewValue = desired, RequestId = requestId,
            Endpoint = "/api/platform/catalog/modules",
            IsSuccess = true, OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
        });
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return ApiResponse<PlatformProductModuleAdminDto>.SuccessResponse(
                MapProductModule(row), "Product module updated.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                "Module changed. Refresh and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Product module configuration conflict for {ModuleId}", id);
            return ApiResponse<PlatformProductModuleAdminDto>.ErrorResponse(
                "Module update conflicted with another operation.", 409);
        }
    }
}
