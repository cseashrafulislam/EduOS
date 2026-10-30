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
        // Never restore dispatched stock at origin while a live receipt exists.
        if (original.SourceType == "TransferDispatch" &&
            await db.InventoryMovements.AsNoTracking().AnyAsync(r => r.TenantId == user.TenantId &&
                r.SourceType == "TransferReceipt" && r.SourceId == original.Id &&
                r.StateCode == "Posted" && !r.IsDeleted &&
                !db.InventoryMovements.Any(v => v.TenantId == user.TenantId &&
                    v.ReversalOfMovementId == r.Id && v.StateCode == "Posted" && !v.IsDeleted), ct))
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Reverse the transfer receipt before cancelling dispatch.", 409);
        if (original.StateCode != "Posted" || original.ReversalOfMovementId.HasValue ||
            original.MovementTypeCode is not ("Receipt" or "Issue" or "Transfer") ||
            !Version(original.RowVersion, request.RowVersion) ||
            await db.InventoryMovements.AnyAsync(x => x.TenantId == user.TenantId &&
                x.ReversalOfMovementId == original.Id && !x.IsDeleted, ct))
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Already reversed or movement changed.", 409);
        return null;
    }
}