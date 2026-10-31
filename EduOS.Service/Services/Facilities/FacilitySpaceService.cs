using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Facilities;

/// <summary>Tenant/campus-authorized operational space registry. Does not represent academic, hostel or stock rooms.</summary>
public sealed class FacilitySpaceService : IFacilitySpaceService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    private readonly ILogger<FacilitySpaceService> logger;

    public FacilitySpaceService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
        ILogger<FacilitySpaceService> logger)
    {
        this.db = db; this.user = user; this.clock = clock; this.logger = logger;
    }

    private bool Readable => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("FacilitiesManager") ||
         user.IsInRole("FacilitiesTechnician") || user.IsInRole("FacilitiesApprover"));
    private bool Writable => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("FacilitiesManager"));
    private bool AllCampuses => user.IsTenantAdmin || user.IsInRole("Principal");
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Facilities access denied.", 403);
    private IQueryable<long> AllowedCampusIds() =>
        db.Campuses.AsNoTracking().Where(c => c.TenantId == user.TenantId && c.IsActive && !c.IsDeleted &&
            (AllCampuses || db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                a.UserId == user.UserId && a.CampusId == c.Id && a.IsActive && !a.IsDeleted)))
            .Select(c => c.Id);
    private static FacilitySpaceDto Map(FacilitySpace x) => new()
    {
        Id = x.Id, CampusId = x.CampusId, Code = x.Code, Name = x.Name, Address = x.Address,
        IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private static bool MatchesVersion(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }

    public async Task<ApiResponse<PagedResult<FacilitySpaceDto>>> ListAsync(
        long? campusId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId) return Denied<PagedResult<FacilitySpaceDto>>();
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100 || campusId is <= 0 ||
            search?.Length > 100) return ApiResponse<PagedResult<FacilitySpaceDto>>.ErrorResponse("Invalid page or filter.");
        var allowed = AllowedCampusIds();
        if (campusId.HasValue && !await allowed.ContainsAsync(campusId.Value, ct))
            return Denied<PagedResult<FacilitySpaceDto>>();
        var query = db.Set<FacilitySpace>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && allowed.Contains(x.CampusId));
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CampusId).ThenBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilitySpaceDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = count, Items = rows.Select(Map).ToList()
        });
    }

    public async Task<ApiResponse<FacilitySpaceDto>> SaveAsync(
        long? id, SaveFacilitySpaceRequestDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<FacilitySpaceDto>();
        if (request == null || id is <= 0 || request.CampusId <= 0)
            return ApiResponse<FacilitySpaceDto>.ErrorResponse("Valid space details required.");
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        var name = (request.Name ?? "").Trim();
        var address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (code.Length is < 1 or > 50 || name.Length is < 1 or > 150 || address?.Length > 500 ||
            !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            return ApiResponse<FacilitySpaceDto>.ErrorResponse("Invalid code, name or address.");
        var allowed = AllowedCampusIds();
        if (!await allowed.ContainsAsync(request.CampusId, ct)) return Denied<FacilitySpaceDto>();
        try
        {
            FacilitySpace space;
            if (id.HasValue)
            {
                var existing = await db.Set<FacilitySpace>().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
                if (existing == null) return ApiResponse<FacilitySpaceDto>.ErrorResponse("Space not found.", 404);
                if (!await allowed.ContainsAsync(existing.CampusId, ct)) return Denied<FacilitySpaceDto>();
                if (!MatchesVersion(existing.RowVersion, request.RowVersion))
                    return ApiResponse<FacilitySpaceDto>.ErrorResponse("Space changed; reload.", 409);
                space = existing;
            }
            else
            {
                var existing = await db.Set<FacilitySpace>().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.CampusId == request.CampusId && x.Code == code, ct);
                if (existing != null)
                    return existing.Name == name && existing.Address == address && existing.IsActive == request.IsActive
                        ? ApiResponse<FacilitySpaceDto>.SuccessResponse(Map(existing), "Existing space returned.")
                        : ApiResponse<FacilitySpaceDto>.ErrorResponse("Space code already exists.", 409);
                space = new FacilitySpace { TenantId = user.TenantId, CreatedBy = user.UserId,
                    CreatedAt = clock.GetUtcNow().UtcDateTime };
                db.Set<FacilitySpace>().Add(space);
            }
            space.CampusId = request.CampusId; space.Code = code; space.Name = name;
            space.Address = address; space.IsActive = request.IsActive;
            if (id.HasValue) { space.UpdatedAt = clock.GetUtcNow().UtcDateTime; space.UpdatedBy = user.UserId; }
            await db.SaveChangesAsync(ct);
            var result = ApiResponse<FacilitySpaceDto>.SuccessResponse(Map(space), "Space saved.");
            if (!id.HasValue) result.StatusCode = 201;
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<FacilitySpaceDto>.ErrorResponse("Space changed; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Facilities space constraint conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<FacilitySpaceDto>.ErrorResponse("Space code or campus conflict.", 409);
        }
    }
}
