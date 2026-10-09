using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryCatalogService
{
    public async Task<ApiResponse<IReadOnlyList<InventoryLocationDto>>> GetLocationsAsync(long? campusId, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<InventoryLocationDto>>();
        if (campusId is <= 0) return ApiResponse<IReadOnlyList<InventoryLocationDto>>.ErrorResponse("Invalid campus.");
        if (campusId.HasValue && !await CanAccessCampusAsync(campusId.Value, ct))
            return Denied<IReadOnlyList<InventoryLocationDto>>();
        var tenantId = _user.TenantId;
        var userId = _user.UserId;
        var rows = _db.InventoryLocations.AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted);
        if (campusId.HasValue) rows = rows.Where(x => x.CampusId == campusId);
        if (!CanAccessAllCampuses())
            rows = rows.Where(x => x.CampusId == null || _db.UserCampusAccesses.Any(a =>
                a.TenantId == tenantId && a.UserId == userId && a.CampusId == x.CampusId &&
                a.IsActive && !a.IsDeleted));
        var result = await rows.OrderBy(x => x.Code).ThenBy(x => x.Id).Take(200).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<InventoryLocationDto>>.SuccessResponse(result.Select(Map).ToList());
    }
}
