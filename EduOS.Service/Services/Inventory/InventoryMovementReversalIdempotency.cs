using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    private async Task<ApiResponse<InventoryMovementDto>?> ReversalConflictAsync(
        InventoryMovement original, ReverseInventoryMovementRequestDto request, CancellationToken ct)
    {
        var existing = await db.InventoryMovements.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
            x.ClientRequestId == request.ClientRequestId, ct);
        if (existing != null) return existing.ReversalOfMovementId == original.Id && existing.Remarks == request.Reason.Trim() ?
            ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(existing, ct), "Existing reversal returned.") :
            ApiResponse<InventoryMovementDto>.ErrorResponse("Request ID belongs to another operation.", 409);
        if (original.SourceType == "AssetCapitalization")
            return ApiResponse<InventoryMovementDto>.ErrorResponse(
                "Capitalized asset stock cannot be reversed independently of its posted Finance journal and AssetBook.", 409);
        if (original.SourceType == "ProcurementSupplierReturn")
            return ApiResponse<InventoryMovementDto>.ErrorResponse(
                "Supplier return stock cannot be reversed without its approved credit-note Finance GL correction.", 409);
        // Never restore dispatched stock at origin while a live receipt exists.
        if (original.SourceType == "TransferDispatch" &&
            await db.InventoryMovements.AsNoTracking().AnyAsync(r => r.TenantId == user.TenantId &&
                r.SourceType == "TransferReceipt" && r.SourceId == original.Id &&
                r.StateCode == "Posted" && !r.IsDeleted &&
                !db.InventoryMovements.Any(v => v.TenantId == user.TenantId &&
                    v.ReversalOfMovementId == r.Id && v.StateCode == "Posted" && !v.IsDeleted), ct))
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Reverse the transfer receipt before cancelling dispatch.", 409);
        // A stock reversal cannot orphan a submitted or posted supplier bill. Undo
        // the payable through Finance's reviewed contra before reversing stock.
        if (original.SourceType == "ProcurementGoodsReceipt")
        {
            var receiptIds = db.ProcurementGoodsReceipts.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.PostedMovementId == original.Id).Select(x => x.Id);
            var receiptLineIds = db.ProcurementGoodsReceiptLines.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && receiptIds.Contains(x.GoodsReceiptId)).Select(x => x.Id);
            if (await db.ProcurementSupplierInvoiceLines.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && receiptLineIds.Contains(x.GoodsReceiptLineId) && !x.IsDeleted &&
                db.ProcurementSupplierInvoices.Any(h => h.TenantId == user.TenantId &&
                    h.Id == x.SupplierInvoiceId && !h.IsDeleted &&
                    (h.StateCode == "Submitted" || h.StateCode == "Posted")), ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse(
                    "Submitted/posted supplier invoices reserve this goods receipt; reverse the payable first.", 409);
            if (await db.AssetCapitalizationCommands.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && !x.IsDeleted && x.StateCode == "Posted" &&
                receiptLineIds.Contains(x.GoodsReceiptLineId), ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse(
                    "Posted asset capitalization consumes this receipt; follow approved asset correction before reversing it.", 409);
        }
        if (original.StateCode != "Posted" || original.ReversalOfMovementId.HasValue ||
            original.MovementTypeCode is not ("Receipt" or "Issue" or "Transfer") ||
            !Version(original.RowVersion, request.RowVersion) ||
            await db.InventoryMovements.AnyAsync(x => x.TenantId == user.TenantId &&
                x.ReversalOfMovementId == original.Id && !x.IsDeleted, ct))
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Already reversed or movement changed.", 409);
        return null;
    }
}
