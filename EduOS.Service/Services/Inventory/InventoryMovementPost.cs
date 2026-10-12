using Microsoft.Extensions.Logging;
using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    public async Task<ApiResponse<InventoryMovementDto>> PostMovementAsync(Guid movementReference, PostInventoryMovementRequestDto request, CancellationToken ct = default)
    {
        if (!Postable) return Denied<InventoryMovementDto>();
        if (request == null) return ApiResponse<InventoryMovementDto>.ErrorResponse("Row version required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var m = await db.InventoryMovements.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == movementReference && !x.IsDeleted, ct);
            if (m == null || !await LocationsAllowed(m.FromLocationId, m.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement not found.", 404);
            if (m.StateCode == "Posted") return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(m, ct), "Already posted.");
            if (m.StateCode != "Draft" || !Version(m.RowVersion, request.RowVersion) ||
                m.MovementTypeCode is not ("Receipt" or "Issue" or "Transfer"))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement changed or cannot be posted.", 409);
            var (error, lines) = await CheckPostingAsync(m, ct);
            if (error != null) return ApiResponse<InventoryMovementDto>.ErrorResponse(error, 409);
            var now = clock.GetUtcNow().UtcDateTime;
            m.StateCode = "Posted"; m.PostedAt = now; m.PostedByUserId = user.UserId;
            m.UpdatedAt = now; m.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(Map(m, lines), "Stock posted.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Inventory post conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Posting conflict; reload.", 409); }
    }
}