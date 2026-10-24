using System.Data;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

// Host-only metadata configuration; codes and existing tenant profiles are immutable.
public partial class PlatformCatalogService
{
    private static string InstitutionTypeMetadataSnapshot(
        string name, string? bangla, string? description, AcademicCycleType cycle, int order) =>
        JsonSerializer.Serialize(new { Name = name, NameBangla = bangla, Description = description,
            AcademicCycleType = cycle.ToString(), DisplayOrder = order });

    public async Task<ApiResponse<PlatformInstitutionTypeAdminDto>> SaveInstitutionTypeMetadataAsync(
        long id, SaveInstitutionTypeMetadataRequestDto request, CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Platform administrator required.", 403);
        if (id <= 0 || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) || string.IsNullOrWhiteSpace(request.Name))
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse(
                "Institution type, request ID, row version and name are required.", 400);

        var name = request.Name.Trim();
        var bangla = string.IsNullOrWhiteSpace(request.NameBangla) ? null : request.NameBangla.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (name.Length > 150 || bangla?.Length > 150 || description?.Length > 1000 ||
            request.DisplayOrder is < 0 or > 100000 ||
            !Enum.TryParse<AcademicCycleType>(request.AcademicCycleType, true, out var cycle) ||
            !Enum.IsDefined(cycle))
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse(
                "Invalid institution metadata or academic-cycle default.", 400);

        byte[] supplied;
        try { supplied = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException)
        { return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Invalid row version.", 400); }
        if (supplied.Length == 0)
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Invalid row version.", 400);

        var db = _adminDb!;
        var user = _adminUser!;
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var row = await db.InstitutionTypeDefinitions.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (row == null)
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Institution type not found.", 404);

        var desired = InstitutionTypeMetadataSnapshot(name, bangla, description, cycle, request.DisplayOrder);
        var current = InstitutionTypeMetadataSnapshot(
            row.Name, row.NameBangla, row.Description, row.DefaultAcademicCycle, row.DisplayOrder);
        var requestId = request.RequestId.ToString("D");
        // Host-scoped audit lookup only; no tenant data is returned or modified.
        var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == null && !x.IsDeleted && x.EntityName == nameof(InstitutionTypeDefinition) &&
            x.EntityId == id && x.Action == "InstitutionType.MetadataChanged" &&
            x.RequestId == requestId, ct);
        if (prior != null)
            return prior.UserId == user.UserId && prior.NewValue == desired && current == desired
                ? ApiResponse<PlatformInstitutionTypeAdminDto>.SuccessResponse(MapAdmin(row), "Already applied.")
                : ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse(
                    "Request ID was already used or institution metadata changed.", 409);

        if (!row.RowVersion.AsSpan().SequenceEqual(supplied))
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse(
                "Institution type changed. Refresh and retry.", 409);
        if (current == desired)
            return ApiResponse<PlatformInstitutionTypeAdminDto>.SuccessResponse(
                MapAdmin(row), "Already in requested state.");

        var now = DateTime.UtcNow;
        row.Name = name;
        row.NameBangla = bangla;
        row.Description = description;
        row.DefaultAcademicCycle = cycle;
        row.DisplayOrder = request.DisplayOrder;
        row.UpdatedAt = now;
        row.UpdatedBy = user.UserId;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = null, UserId = user.UserId, Action = "InstitutionType.MetadataChanged",
            EntityName = nameof(InstitutionTypeDefinition), EntityId = id,
            OldValue = current, NewValue = desired, RequestId = requestId,
            Endpoint = "/api/platform/catalog/institution-types",
            IsSuccess = true, OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
        });
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return ApiResponse<PlatformInstitutionTypeAdminDto>.SuccessResponse(
                MapAdmin(row), "Institution type metadata updated.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse(
                "Institution type changed. Refresh and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Institution type metadata conflict for {InstitutionTypeId}", id);
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse(
                "Institution type update conflicted with another operation.", 409);
        }
    }
}
