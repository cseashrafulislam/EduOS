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
        if (!Postable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (request == null) return ApiResponse<InventoryMovementDto>.ErrorResponse("Row version required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var m = await db.InventoryMovements.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == movementReference && !x.IsDeleted, ct);
            if (m == null || !await LocationsAllowed(m.FromLocationId, m.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement not found.", 404);
            if (m.StateCode == "Posted") return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(m, ct), "Already posted.");
            if (m.StateCode is not ("Draft" or "Reserved") || !Version(m.RowVersion, request.RowVersion) ||
                m.MovementTypeCode is not ("Receipt" or "Issue" or "Transfer"))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement changed or cannot be posted.", 409);
            if (m.StateCode == "Reserved" && m.ReservationExpiresAtUtc.HasValue &&
                m.ReservationExpiresAtUtc.Value <= clock.GetUtcNow().UtcDateTime)
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation deadline passed; expire or release the hold before further posting.", 409);
            var (error, lines) = await CheckPostingAsync(m, ct);
            if (error != null) return ApiResponse<InventoryMovementDto>.ErrorResponse(error, 409);
            var now = clock.GetUtcNow().UtcDateTime;
            m.StateCode = "Posted"; m.PostedAt = now; m.PostedByUserId = user.UserId;
            m.UpdatedAt = now; m.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(Map(m, lines), "Stock posted.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960)
        { logger.LogWarning(ex, "Inventory post SQL race"); return ApiResponse<InventoryMovementDto>.ErrorResponse("Posting conflict; reload.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Inventory post conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Posting conflict; reload.", 409); }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        { logger.LogWarning(ex, "Inventory post wrapped SQL race");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Posting conflict; reload.", 409); }
    }
}