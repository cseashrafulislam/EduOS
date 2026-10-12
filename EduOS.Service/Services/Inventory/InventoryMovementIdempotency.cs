using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    private async Task<ApiResponse<InventoryMovementDto>?> ExistingCreateAsync(CreateInventoryMovementRequestDto request, CancellationToken ct)
    {
        var existing = await db.InventoryMovements.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
            x.ClientRequestId == request.ClientRequestId, ct);
        if (existing == null) return null;
        var old = await db.InventoryMovementLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.InventoryMovementId == existing.Id && !x.IsDeleted).OrderBy(x => x.LineNo).ToListAsync(ct);
        var same = existing.MovementTypeCode == request.MovementTypeCode.Trim() &&
            existing.FromLocationId == request.FromLocationId && existing.ToLocationId == request.ToLocationId &&
            existing.SourceType == request.SourceType?.Trim() && existing.Remarks == request.Remarks!.Trim() &&
            old.Count == request.Lines.Count && old.Select((x, i) =>
                x.InventoryItemId == request.Lines[i].InventoryItemId && x.Quantity == request.Lines[i].Quantity &&
                (existing.StateCode == "Posted" || x.UnitCost == request.Lines[i].UnitCost) &&
                (x.LotOrBatchNo ?? "") == (request.Lines[i].LotOrBatchNo?.Trim() ?? "")).All(x => x);
        return same ? ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(existing, ct), "Existing request returned.") :
            ApiResponse<InventoryMovementDto>.ErrorResponse("Request ID belongs to different movement details.", 409);
    }
}