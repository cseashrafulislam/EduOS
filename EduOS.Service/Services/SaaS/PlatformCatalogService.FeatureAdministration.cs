using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

// Feature definitions belong to the existing host PlatformCatalogService.
// Codes and module/plan assignments are immutable in this metadata workflow.
public partial class PlatformCatalogService
{
    private static PlatformFeatureAdminDto MapFeature(Feature row) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name,
        Description = row.Description, IsActive = row.IsActive,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static string FeatureSnapshot(string name, string? description, bool active) =>
        JsonSerializer.Serialize(new { Name = name, Description = description, IsActive = active });

    public async Task<ApiResponse<IReadOnlyList<PlatformFeatureAdminDto>>> GetAdminFeaturesAsync(
        CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<IReadOnlyList<PlatformFeatureAdminDto>>.ErrorResponse(
                "Platform administrator required.", 403);
        var rows = await _adminDb!.Features.AsNoTracking()
            .Where(x => !x.IsDeleted).OrderBy(x => x.Code).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<PlatformFeatureAdminDto>>.SuccessResponse(
            rows.Select(MapFeature).ToList());
    }

    public async Task<ApiResponse<PlatformFeatureAdminDto>> SaveFeatureConfigurationAsync(
        long id, SavePlatformFeatureRequestDto request, CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse(
                "Platform administrator required.", 403);
        if (id <= 0 || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            string.IsNullOrWhiteSpace(request.Name))
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse(
                "Feature, request ID, row version and name are required.", 400);

        var name = request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null : request.Description.Trim();
        if (name.Length > 150 || description?.Length > 500)
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse(
                "Feature name or description exceeds its maximum length.", 400);

        byte[] supplied;
        try { supplied = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException)
        {
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse("Invalid row version.", 400);
        }
        if (supplied.Length == 0)
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse("Invalid row version.", 400);

        var db = _adminDb!;
        var user = _adminUser!;
        var row = await db.Features.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (row == null)
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse("Feature not found.", 404);

        var desired = FeatureSnapshot(name, description, request.IsActive);
        var current = FeatureSnapshot(row.Name, row.Description, row.IsActive);
        var requestId = request.RequestId.ToString("D");
        // Host-only audit deduplication: no tenant-scoped operation uses this bypass.
        var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == null && !x.IsDeleted && x.EntityName == nameof(Feature) &&
            x.EntityId == id && x.Action == "Feature.ConfigurationChanged" &&
            x.RequestId == requestId, ct);
        if (prior != null)
            return prior.UserId == user.UserId && prior.NewValue == desired && current == desired
                ? ApiResponse<PlatformFeatureAdminDto>.SuccessResponse(MapFeature(row), "Already applied.")
                : ApiResponse<PlatformFeatureAdminDto>.ErrorResponse(
                    "Request ID was already used or the feature changed.", 409);

        if (!row.RowVersion.AsSpan().SequenceEqual(supplied))
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse(
                "Feature changed. Refresh and retry.", 409);
        if (current == desired)
            return ApiResponse<PlatformFeatureAdminDto>.SuccessResponse(
                MapFeature(row), "Already in requested state.");

        var now = DateTime.UtcNow;
        row.Name = name;
        row.Description = description;
        row.IsActive = request.IsActive;
        row.UpdatedAt = now;
        row.UpdatedBy = user.UserId;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = null, UserId = user.UserId,
            Action = "Feature.ConfigurationChanged",
            EntityName = nameof(Feature), EntityId = id,
            OldValue = current, NewValue = desired, RequestId = requestId,
            Endpoint = "/api/platform/catalog/features",
            IsSuccess = true, OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return ApiResponse<PlatformFeatureAdminDto>.SuccessResponse(
                MapFeature(row), "Feature configuration updated.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse(
                "Feature changed. Refresh and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Feature configuration conflict for {FeatureId}", id);
            return ApiResponse<PlatformFeatureAdminDto>.ErrorResponse(
                "Feature update conflicted with another operation.", 409);
        }
    }
}
