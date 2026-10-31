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
                join order in _db.ProcurementPurchaseOrders.AsNoTracking()
                    on new { receipt.TenantId, Id = receipt.PurchaseOrderId }
                    equals new { order.TenantId, order.Id }
                join poLine in _db.ProcurementPurchaseOrderLines.AsNoTracking()
                    on new { line.TenantId, Id = line.PurchaseOrderLineId }
                    equals new { poLine.TenantId, poLine.Id }
                join location in _db.InventoryLocations.AsNoTracking()
                    on new { receipt.TenantId, Id = receipt.LocationId }
                    equals new { location.TenantId, location.Id }
                join movement in _db.InventoryMovements.AsNoTracking()
                    on new { receipt.TenantId, Id = receipt.PostedMovementId }
                    equals new { movement.TenantId, Id = (long?)movement.Id }
                where line.TenantId == Tenant && receipt.TenantId == Tenant &&
                    movement.TenantId == Tenant && !line.IsDeleted && !receipt.IsDeleted &&
                    !order.IsDeleted && !poLine.IsDeleted && !movement.IsDeleted &&
                    order.StateCode == "Approved" && order.CurrencyCode == "BDT" &&
                    order.CampusId == receipt.CampusId &&
                    poLine.PurchaseOrderId == order.Id &&
                    poLine.InventoryItemId == line.InventoryItemId &&
                    poLine.Quantity == line.ApprovedPoQuantity &&
                    poLine.UnitPrice == line.ApprovedPoUnitPrice &&
                    line.Quantity <= poLine.Quantity &&
                    location.CampusId == receipt.CampusId &&
                    receipt.CampusId == asset.CampusId.Value &&
                    line.InventoryItemId == asset.InventoryItemId!.Value &&
                    receipt.StateCode == "Posted" && movement.StateCode == "Posted" &&
                    // A Posted header alone does not prove independent approval.
                    receipt.CreatedBy.HasValue && receipt.SubmittedByUserId.HasValue &&
                    receipt.ReviewedByUserId.HasValue && receipt.SubmitRequestId.HasValue &&
                    receipt.ReviewRequestId.HasValue && receipt.SubmittedAtUtc.HasValue &&
                    receipt.ReviewedAtUtc.HasValue &&
                    receipt.SubmittedAtUtc <= receipt.ReviewedAtUtc &&
                    receipt.ReviewedByUserId != receipt.CreatedBy &&
                    receipt.ReviewedByUserId != receipt.SubmittedByUserId &&
                    receipt.ReviewReason != null && receipt.ReviewReason.Length >= 10 &&
                    (receipt.ValuationMode == "Automatic" ||
                     receipt.ValuationMode == "ManualFinal" ||
                     receipt.ValuationMode == "AutoPlusManual") &&
                    movement.PostedByUserId == receipt.ReviewedByUserId &&
                    movement.PostedAt.HasValue &&
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
                    ReviewedAtUtc = receipt.ReviewedAtUtc,
                    ValuationMode = receipt.ValuationMode ?? string.Empty
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
