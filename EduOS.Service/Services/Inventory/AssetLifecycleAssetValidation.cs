using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    private async Task<string?> ValidateAssetAsync(SaveAssetRequestDto r, CancellationToken ct)
    {
        var tag = Code(r.AssetTag); var name = Text(r.Name); var serial = Text(r.SerialNumber);
        if (r.CampusId is <= 0 || r.InventoryItemId is <= 0 || r.InventoryLocationId is <= 0 ||
            tag.Length is < 1 or > 100 || name.Length is < 1 or > 200 || serial.Length > 100 ||
            r.AssetCategoryId <= 0 || (r.PurchaseCost.HasValue && !Money(r.PurchaseCost.Value)) ||
            r.PurchaseDate > Today)
            return "Invalid asset identity, date, campus, or money precision.";
        if (!await CampusAllowedAsync(r.CampusId, ct)) return "Asset campus access denied.";
        if (!await _db.AssetCategories.AsNoTracking().AnyAsync(x => x.TenantId == Tenant && x.Id == r.AssetCategoryId && x.IsActive && !x.IsDeleted, ct))
            return "Select an active asset category.";
        if (r.InventoryItemId.HasValue &&
            !await _db.InventoryItems.AsNoTracking().AnyAsync(x => x.TenantId == Tenant && x.Id == r.InventoryItemId &&
                x.IsAssetTracked && x.IsActive && !x.IsDeleted, ct))
            return "Inventory item must be active and asset-tracked.";
        if (r.InventoryLocationId.HasValue &&
            !await _db.InventoryLocations.AsNoTracking().AnyAsync(x => x.TenantId == Tenant && x.Id == r.InventoryLocationId &&
                x.CampusId == r.CampusId && x.IsActive && !x.IsDeleted, ct))
            return "Location must be active and belong to the asset campus.";
        return null;
    }
}
