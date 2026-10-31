using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class AssetLifecycleService
{
    // Read-only, tenant/campus-authorized preflight. Never creates books, stock or Finance journals.
    public async Task<ApiResponse<AssetCapitalizationReadinessDto>> GetCapitalizationReadinessAsync(
        Guid assetReference, CancellationToken ct = default)
    {
        if (!CanRead() || Tenant <= 0 || _db.CurrentTenantId != Tenant)
            return Denied<AssetCapitalizationReadinessDto>();
        if (assetReference == Guid.Empty)
            return Invalid<AssetCapitalizationReadinessDto>("Asset reference required.");

        var asset = await VisibleAssets().AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicId == assetReference, ct);
        if (asset == null)
            return ApiResponse<AssetCapitalizationReadinessDto>.ErrorResponse("Asset not found.", 404);
        if (!await CampusAllowedAsync(asset.CampusId, ct))
            return Denied<AssetCapitalizationReadinessDto>();

        var book = await _db.AssetBooks.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.AssetId == asset.Id && !x.IsDeleted, ct);
        var trackedItem = asset.InventoryItemId.HasValue &&
            await _db.InventoryItems.AsNoTracking().AnyAsync(x => x.TenantId == Tenant &&
                x.Id == asset.InventoryItemId.Value && x.IsAssetTracked &&
                x.IsActive && !x.IsDeleted, ct);

        var result = new AssetCapitalizationReadinessDto
        {
            AssetReference = asset.PublicId,
            AssetTag = asset.AssetTag,
            CampusId = asset.CampusId,
            RegisteredPurchaseCost = asset.PurchaseCost,
            IsAssetTrackedItem = trackedItem,
            HasBook = book != null,
            BookIsActive = book?.IsActive ?? false,
            BookCostBasis = book?.CostBasis,
            BookMethod = book?.MethodCode,
            BookPolicyVersion = book?.PolicyVersion
        };

        if (asset.CampusId is > 0 && trackedItem)
        {
            // Source lines must belong to an approved posted receipt and its still-posted
            // canonical stock movement. A reversed movement is not current evidence.
            var candidates =
                from line in _db.ProcurementGoodsReceiptLines.AsNoTracking()
                join receipt in _db.ProcurementGoodsReceipts.AsNoTracking()
                    on new { line.TenantId, Id = line.GoodsReceiptId }
                    equals new { receipt.TenantId, receipt.Id }
                join movement in _db.InventoryMovements.AsNoTracking()
                    on new { receipt.TenantId, Id = receipt.PostedMovementId }
                    equals new { movement.TenantId, Id = (long?)movement.Id }
                where line.TenantId == Tenant && receipt.TenantId == Tenant &&
                    movement.TenantId == Tenant && !line.IsDeleted && !receipt.IsDeleted &&
                    !movement.IsDeleted && receipt.CampusId == asset.CampusId.Value &&
                    line.InventoryItemId == asset.InventoryItemId!.Value &&
                    receipt.StateCode == "Posted" && movement.StateCode == "Posted" &&
                    movement.SourceType == "ProcurementGoodsReceipt" &&
                    movement.SourceId == receipt.Id && movement.MovementTypeCode == "Receipt" &&
                    movement.ToLocationId == receipt.LocationId &&
                    line.Quantity > 0m && line.PostedUnitCost.HasValue && line.PostedUnitCost > 0m &&
                    _db.InventoryMovementLines.Any(stockLine =>
                        stockLine.TenantId == Tenant && !stockLine.IsDeleted &&
                        stockLine.InventoryMovementId == movement.Id &&
                        stockLine.LineNo == line.LineNo &&
                        stockLine.InventoryItemId == line.InventoryItemId &&
                        stockLine.Quantity == line.Quantity &&
                        stockLine.UnitCost == line.PostedUnitCost.Value &&
                        (stockLine.LotOrBatchNo ?? "") == line.LotOrBatchNo) &&
                    !_db.InventoryMovements.Any(reversal => reversal.TenantId == Tenant &&
                        !reversal.IsDeleted && reversal.StateCode == "Posted" &&
                        reversal.ReversalOfMovementId == movement.Id)
                select new AssetCapitalizationReceiptCandidateDto
                {
                    ReceiptReference = receipt.PublicId,
                    PostedMovementReference = movement.PublicId,
                    PurchaseOrderId = receipt.PurchaseOrderId,
                    LineNo = line.LineNo,
                    Quantity = line.Quantity,
                    PostedUnitCost = line.PostedUnitCost,
                    LotOrBatchNo = line.LotOrBatchNo,
                    ReviewedAtUtc = receipt.ReviewedAtUtc
                };

            result.CandidateLineCount = await candidates.CountAsync(ct);
            result.Candidates = await candidates.OrderByDescending(x => x.ReviewedAtUtc)
                .ThenBy(x => x.ReceiptReference).ThenBy(x => x.LineNo)
                .Take(25).ToListAsync(ct);
            result.CandidateListTruncated = result.CandidateLineCount > result.Candidates.Count;
        }

        result.StatusCode = AssetCapitalizationStatus.Classify(asset.IsActive, book != null,
            asset.CampusId is > 0, trackedItem, asset.PurchaseCost is > 0m,
            result.CandidateLineCount);
        return ApiResponse<AssetCapitalizationReadinessDto>.SuccessResponse(result);
    }
}
