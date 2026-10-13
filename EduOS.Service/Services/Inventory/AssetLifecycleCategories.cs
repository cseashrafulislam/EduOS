using Microsoft.Extensions.Logging;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    public async Task<ApiResponse<AssetCategoryDto>> SaveCategoryAsync(long? categoryId, SaveAssetCategoryRequestDto request, CancellationToken ct = default)
    {
        // Categories are tenant-wide; campus-scoped managers cannot change shared definitions.
        if (!CanWrite() || !AllCampuses()) return Denied<AssetCategoryDto>();
        if (request == null || categoryId is <= 0) return Invalid<AssetCategoryDto>("Invalid category request.");
        var code = Code(request.Code); var name = Text(request.Name);
        if (code.Length is < 1 or > 50 || name.Length is < 1 or > 150)
            return Invalid<AssetCategoryDto>("Category code and name are required.");
        try
        {
            AssetCategory entity;
            if (categoryId.HasValue)
            {
                var existing = await _db.AssetCategories.FirstOrDefaultAsync(x => x.TenantId == Tenant && x.Id == categoryId && !x.IsDeleted, ct);
                if (existing == null) return ApiResponse<AssetCategoryDto>.ErrorResponse("Category not found.", 404);
                if (!Version(existing.RowVersion, request.RowVersion))
                    return ApiResponse<AssetCategoryDto>.ErrorResponse("Category changed; reload.", 409);
                if (!request.IsActive && await _db.Assets.AnyAsync(x => x.TenantId == Tenant && x.AssetCategoryId == existing.Id && !x.IsDeleted && x.IsActive, ct))
                    return ApiResponse<AssetCategoryDto>.ErrorResponse("Deactivate assets before deactivating their category.", 409);
                entity = existing; entity.UpdatedAt = Now; entity.UpdatedBy = _user.UserId;
            }
            else
            {
                entity = new AssetCategory { TenantId = Tenant, CreatedAt = Now, CreatedBy = _user.UserId };
                _db.AssetCategories.Add(entity);
            }
            if (await _db.AssetCategories.AnyAsync(x => x.TenantId == Tenant && x.Code == code && x.Id != entity.Id, ct))
                return ApiResponse<AssetCategoryDto>.ErrorResponse("Category code is already used.", 409);
            entity.Code = code; entity.Name = name; entity.IsActive = request.IsActive;
            await _db.SaveChangesAsync(ct);
            return ApiResponse<AssetCategoryDto>.SuccessResponse(Map(entity), "Asset category saved.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<AssetCategoryDto>.ErrorResponse("Category changed; reload.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Asset category constraint conflict for tenant {Tenant}", Tenant);
            return ApiResponse<AssetCategoryDto>.ErrorResponse("Category conflicts with an existing record.", 409);
        }
    }
}
