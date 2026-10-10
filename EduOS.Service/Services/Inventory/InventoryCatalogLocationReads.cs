using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryCatalogService
{
    private IQueryable<InventoryLocation> VisibleLocations(long? campusId)
    {
        var tenantId = _user.TenantId;
        var userId = _user.UserId;
        var rows = _db.InventoryLocations.AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted);
        if (campusId.HasValue) rows = rows.Where(x => x.CampusId == campusId);
        if (!CanAccessAllCampuses())
            rows = rows.Where(x => x.CampusId == null || _db.UserCampusAccesses.Any(a =>
                a.TenantId == tenantId && a.UserId == userId && a.CampusId == x.CampusId &&
                a.IsActive && !a.IsDeleted &&
                _db.Campuses.Any(c => c.TenantId == tenantId && c.Id == a.CampusId &&
                    c.IsActive && !c.IsDeleted)));
        return rows;
    }

    // Preserve legacy array contract; newer consumers opt in to pagination.
    public async Task<ApiResponse<IReadOnlyList<InventoryLocationDto>>> GetLocationsAsync(long? campusId, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<InventoryLocationDto>>();
        if (campusId is <= 0) return ApiResponse<IReadOnlyList<InventoryLocationDto>>.ErrorResponse("Invalid campus.");
        if (campusId.HasValue && !await CanAccessCampusAsync(campusId.Value, ct))
            return Denied<IReadOnlyList<InventoryLocationDto>>();
        var rows = await VisibleLocations(campusId).OrderBy(x => x.Code).ThenBy(x => x.Id).Take(200).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<InventoryLocationDto>>.SuccessResponse(rows.Select(Map).ToList());
    }

    public async Task<ApiResponse<PagedResult<InventoryLocationDto>>> GetLocationsPageAsync(int page, int pageSize, long? campusId, string? search, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<InventoryLocationDto>>();
        if (campusId is <= 0) return ApiResponse<PagedResult<InventoryLocationDto>>.ErrorResponse("Invalid campus.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        if (campusId.HasValue && !await CanAccessCampusAsync(campusId.Value, ct))
            return Denied<PagedResult<InventoryLocationDto>>();
        var term = Trim(search);
        if (term is { Length: > 100 })
            return ApiResponse<PagedResult<InventoryLocationDto>>.ErrorResponse("Search is too long.");
        var rows = VisibleLocations(campusId);
        if (term != null) rows = rows.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        var total = await rows.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var result = offset > int.MaxValue ? new List<InventoryLocation>() :
            await rows.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<InventoryLocationDto>>.SuccessResponse(new PagedResult<InventoryLocationDto>
        {
            Items = result.Select(Map).ToList(), TotalCount = total, Page = page, PageSize = pageSize
        });
    }
}
