using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    public async Task<ApiResponse<AssetDto>> SaveAssetAsync(Guid? assetReference, SaveAssetRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite()) return Denied<AssetDto>();
        if (request == null || assetReference == Guid.Empty) return Invalid<AssetDto>("Invalid asset request.");
        if (!await CampusAllowedAsync(request.CampusId, ct)) return Denied<AssetDto>();
        var error = await ValidateAssetAsync(request, ct);
        if (error != null) return Invalid<AssetDto>(error);
        var tag = Code(request.AssetTag);
        try
        {
            Asset entity;
            if (assetReference.HasValue)
            {
                var existing = await VisibleAssets().FirstOrDefaultAsync(x => x.PublicId == assetReference, ct);
                if (existing == null) return ApiResponse<AssetDto>.ErrorResponse("Asset not found.", 404);
                if (!await CampusAllowedAsync(existing.CampusId, ct)) return Denied<AssetDto>();
                if (!Version(existing.RowVersion, request.RowVersion))
                    return ApiResponse<AssetDto>.ErrorResponse("Asset changed; reload.", 409);
                // A book may be activated before the asset has ever been assigned.
                // Lock source identity and registered valuation in either case; a
                // future approved valuation correction must be a separate audited flow.
                var history = await _db.AssetAssignments.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.AssetId == existing.Id && !x.IsDeleted, ct);
                var hasBook = await _db.AssetBooks.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.AssetId == existing.Id && !x.IsDeleted, ct);
                if ((history || hasBook) && (existing.AssetTag != tag || existing.CampusId != request.CampusId ||
                    existing.AssetCategoryId != request.AssetCategoryId || existing.InventoryItemId != request.InventoryItemId ||
                    existing.InventoryLocationId != request.InventoryLocationId || existing.PurchaseDate != request.PurchaseDate ||
                    existing.PurchaseCost != request.PurchaseCost))
                    return ApiResponse<AssetDto>.ErrorResponse("Asset source identity and valuation are locked after custody or accounting activation.", 409);
                if (!request.IsActive && existing.StateCode == "Assigned")
                    return ApiResponse<AssetDto>.ErrorResponse("Return assigned asset before deactivation.", 409);
                entity = existing; entity.UpdatedAt = Now; entity.UpdatedBy = _user.UserId;
            }
            else
            {
                entity = new Asset { TenantId = Tenant, PublicId = Guid.NewGuid(), CreatedAt = Now, CreatedBy = _user.UserId };
                _db.Assets.Add(entity);
            }
            if (await _db.Assets.AsNoTracking().AnyAsync(x => x.TenantId == Tenant && x.AssetTag == tag && x.Id != entity.Id, ct))
                return ApiResponse<AssetDto>.ErrorResponse("Asset tag is already registered.", 409);
            entity.AssetTag = tag; entity.Name = Text(request.Name);
            entity.SerialNumber = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : Text(request.SerialNumber);
            entity.AssetCategoryId = request.AssetCategoryId; entity.CampusId = request.CampusId;
            entity.InventoryItemId = request.InventoryItemId; entity.InventoryLocationId = request.InventoryLocationId;
            entity.PurchaseDate = request.PurchaseDate; entity.PurchaseCost = request.PurchaseCost;
            entity.IsActive = request.IsActive;
            await _db.SaveChangesAsync(ct);
            return ApiResponse<AssetDto>.SuccessResponse(Map(entity), "Asset saved.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<AssetDto>.ErrorResponse("Asset changed; reload.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Asset constraint conflict for tenant {Tenant}", Tenant);
            return ApiResponse<AssetDto>.ErrorResponse("Asset conflicts with an existing record.", 409);
        }
    }
}
