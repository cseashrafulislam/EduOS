using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class AssetLifecycleService
{
    public async Task<ApiResponse<PagedResult<AssetMaintenanceDto>>> GetMaintenanceHistoryAsync(
        Guid assetReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<AssetMaintenanceDto>>();
        if (assetReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Invalid<PagedResult<AssetMaintenanceDto>>("Invalid asset reference or paging parameters.");

        // VisibleAssets enforces tenant and current campus grants before any maintenance is queried.
        var asset = await VisibleAssets().AsNoTracking()
            .Where(x => x.PublicId == assetReference)
            .Select(x => new { x.Id, x.PublicId }).FirstOrDefaultAsync(ct);
        if (asset == null)
            return ApiResponse<PagedResult<AssetMaintenanceDto>>.ErrorResponse("Asset not found.", 404);

        var query = _db.AssetMaintenances.AsNoTracking()
            .Where(x => x.TenantId == Tenant && !x.IsDeleted && x.AssetId == asset.Id);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.MaintenanceDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<AssetMaintenanceDto>>.SuccessResponse(
            new PagedResult<AssetMaintenanceDto>
            {
                Items = rows.Select(x => Map(x, asset.PublicId)).ToList(),
                TotalCount = count, Page = page, PageSize = pageSize
            });
    }
}
