using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    public async Task<ApiResponse<IReadOnlyList<AssetCategoryDto>>> GetCategoriesAsync(CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<AssetCategoryDto>>();
        var rows = await _db.AssetCategories.AsNoTracking().Where(x => x.TenantId == Tenant && !x.IsDeleted)
            .OrderBy(x => x.Code).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AssetCategoryDto>>.SuccessResponse(rows.Select(Map).ToList());
    }
    public async Task<ApiResponse<PagedResult<AssetDto>>> GetAssetsAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<AssetDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Invalid<PagedResult<AssetDto>>("Invalid paging or search parameters.");
        var q = VisibleAssets().AsNoTracking();
        var term = Text(search);
        if (term.Length > 0)
            q = q.Where(x => x.AssetTag.Contains(term) || x.Name.Contains(term) ||
                (x.SerialNumber != null && x.SerialNumber.Contains(term)));
        var count = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.AssetTag).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<AssetDto>>.SuccessResponse(new PagedResult<AssetDto>
        {
            Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
        });
    }
    public async Task<ApiResponse<IReadOnlyList<AssetAssignmentDto>>> GetAssignmentHistoryAsync(Guid assetReference, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<AssetAssignmentDto>>();
        var asset = await VisibleAssets().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == assetReference, ct);
        if (asset == null) return ApiResponse<IReadOnlyList<AssetAssignmentDto>>.ErrorResponse("Asset not found.", 404);
        var rows = await _db.AssetAssignments.AsNoTracking().Where(x => x.TenantId == Tenant && x.AssetId == asset.Id && !x.IsDeleted)
            .OrderByDescending(x => x.AssignedOn).ThenByDescending(x => x.Id).Take(501).ToListAsync(ct);
        if (rows.Count > 500)
            return ApiResponse<IReadOnlyList<AssetAssignmentDto>>.ErrorResponse("History exceeds 500 entries; paged history integration is required.", 409);
        var result = new List<AssetAssignmentDto>();
        foreach (var row in rows) result.Add(await MapAssignmentAsync(row, asset.PublicId, ct));
        return ApiResponse<IReadOnlyList<AssetAssignmentDto>>.SuccessResponse(result);
    }
}
