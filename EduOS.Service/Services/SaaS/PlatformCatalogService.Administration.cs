using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

// Host-only catalog lifecycle; the existing PlatformCatalogService remains the canonical owner.
public partial class PlatformCatalogService
{
    private EduOSDbContext? _adminDb;
    private ICurrentUserService? _adminUser;

    // Existing four-argument constructor is retained for current catalog consumers/tests.
    // DI selects this longer constructor and supplies the host security context.
    public PlatformCatalogService(
        IGenericRepository<InstitutionTypeDefinition> institutionTypeRepository,
        IGenericRepository<InstitutionTypeModule> institutionTypeModuleRepository,
        IGenericRepository<ProductModule> moduleRepository,
        ILogger<PlatformCatalogService> logger,
        EduOSDbContext db,
        ICurrentUserService user)
        : this(institutionTypeRepository, institutionTypeModuleRepository, moduleRepository, logger)
    {
        _adminDb = db;
        _adminUser = user;
    }

    private bool CanAdministerCatalog =>
        _adminDb != null && _adminUser?.IsAuthenticated == true && _adminUser.IsSuperAdmin;

    private static PlatformInstitutionTypeAdminDto MapAdmin(InstitutionTypeDefinition row) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name,
        AcademicCycleType = row.DefaultAcademicCycle.ToString(),
        DisplayOrder = row.DisplayOrder, IsActive = row.IsActive,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    public async Task<ApiResponse<IReadOnlyList<PlatformInstitutionTypeAdminDto>>> GetAdminInstitutionTypesAsync(
        CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<IReadOnlyList<PlatformInstitutionTypeAdminDto>>.ErrorResponse("Platform administrator required.", 403);
        var rows = await _adminDb!.InstitutionTypeDefinitions.AsNoTracking()
            .Where(x => !x.IsDeleted).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .ToListAsync(ct);
        return ApiResponse<IReadOnlyList<PlatformInstitutionTypeAdminDto>>.SuccessResponse(rows.Select(MapAdmin).ToList());
    }

    public async Task<ApiResponse<PlatformInstitutionTypeAdminDto>> ChangeInstitutionTypeActivationAsync(
        long id, ChangeInstitutionTypeActivationRequestDto request, CancellationToken ct = default)
    {
        if (!CanAdministerCatalog)
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Platform administrator required.", 403);
        if (id <= 0 || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Valid reference, request ID and row version are required.");

        var db = _adminDb!;
        var user = _adminUser!;
        var requestId = request.RequestId.ToString("D");
        var target = request.IsActive ? "Active" : "Inactive";
        InstitutionTypeDefinition? row = null;
        AuditLog? audit = null;
        try
        {
            // Global catalog is host-owned. This scoped bypass is only for host audit
            // deduplication: normal tenant APIs never invoke this method.
            row = await db.InstitutionTypeDefinitions.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (row == null)
                return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Institution type not found.", 404);

            var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == null && !x.IsDeleted && x.EntityName == nameof(InstitutionTypeDefinition) &&
                x.EntityId == id && x.Action == "InstitutionType.ActivationChanged" &&
                x.RequestId == requestId, ct);
            if (prior != null)
                return prior.UserId == user.UserId && prior.NewValue == target && row.IsActive == request.IsActive
                    ? ApiResponse<PlatformInstitutionTypeAdminDto>.SuccessResponse(MapAdmin(row), "Already applied.")
                    : ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Request ID was already used or the state changed.", 409);

            byte[] supplied;
            try { supplied = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException)
            { return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Invalid row version.", 400); }
            if (supplied.Length == 0 || !row.RowVersion.AsSpan().SequenceEqual(supplied))
                return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Catalog entry changed. Reload and retry.", 409);
            if (row.IsActive == request.IsActive)
                return ApiResponse<PlatformInstitutionTypeAdminDto>.SuccessResponse(MapAdmin(row), "Already in requested state.");

            var now = DateTime.UtcNow;
            var previous = row.IsActive ? "Active" : "Inactive";
            row.IsActive = request.IsActive;
            row.UpdatedAt = now;
            row.UpdatedBy = user.UserId;
            audit = new AuditLog
            {
                TenantId = null, UserId = user.UserId,
                Action = "InstitutionType.ActivationChanged",
                EntityName = nameof(InstitutionTypeDefinition), EntityId = id,
                OldValue = previous, NewValue = target, RequestId = requestId,
                Endpoint = "/api/platform/catalog/institution-types",
                IsSuccess = true, OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
            };
            db.AuditLogs.Add(audit);
            await db.SaveChangesAsync(ct);
            return ApiResponse<PlatformInstitutionTypeAdminDto>.SuccessResponse(MapAdmin(row), "Institution type availability updated.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            if (audit != null) db.Entry(audit).State = EntityState.Detached;
            if (row != null) db.Entry(row).State = EntityState.Detached;
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Catalog entry changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            if (audit != null) db.Entry(audit).State = EntityState.Detached;
            if (row != null) db.Entry(row).State = EntityState.Detached;
            _logger.LogWarning(ex, "Catalog activation conflict for institution type {Id}", id);
            return ApiResponse<PlatformInstitutionTypeAdminDto>.ErrorResponse("Catalog update conflicted with another operation.", 409);
        }
    }
}
