using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Explicit per-reservation deadline. There is intentionally no background
/// expiry worker: due holds remain protected until an authorized atomic expiry.
/// </summary>
public sealed partial class InventoryMovementService
{
    public async Task<ApiResponse<InventoryMovementDto>> ScheduleReservationExpiryAsync(
        Guid movementReference, ScheduleInventoryReservationExpiryRequestDto request,
        CancellationToken ct = default)
    {
        if (!Postable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (movementReference == Guid.Empty || request == null ||
            request.ExpiresAtUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<InventoryMovementDto>.ErrorResponse("UTC deadline and row version required.", 400);
        var now = clock.GetUtcNow().UtcDateTime;
        if (request.ExpiresAtUtc <= now)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Deadline must be in the future.", 422);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var movement = await db.InventoryMovements.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == movementReference && !x.IsDeleted, ct);
            if (movement == null || !await LocationsAllowed(movement.FromLocationId, movement.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation not found.", 404);
            if (movement.ReservationExpiresAtUtc.HasValue)
                return movement.ReservationExpiresAtUtc.Value == request.ExpiresAtUtc
                    ? ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Deadline already scheduled.")
                    : ApiResponse<InventoryMovementDto>.ErrorResponse("Deadline is immutable; release and recreate to correct.", 409);
            if (movement.StateCode != "Reserved" || movement.MovementTypeCode is not ("Issue" or "Transfer") ||
                !Version(movement.RowVersion, request.RowVersion))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation changed or is not active.", 409);
            movement.ReservationExpiresAtUtc = request.ExpiresAtUtc;
            movement.UpdatedAt = now;
            movement.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Deadline scheduled.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Inventory expiry policy persistence conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Deadline conflict; reload.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960 or 51061)
        {
            logger.LogWarning(ex, "Inventory expiry policy SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Deadline conflict; reload.", 409);
        }
    }

    public async Task<ApiResponse<InventoryMovementDto>> ExpireReservationAsync(
        Guid movementReference, ExpireInventoryReservationRequestDto request,
        CancellationToken ct = default)
    {
        if (!Postable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (movementReference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 400)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Row version and reason (10-400 characters) required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var movement = await db.InventoryMovements.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == movementReference && !x.IsDeleted, ct);
            if (movement == null || !await LocationsAllowed(movement.FromLocationId, movement.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation not found.", 404);
            if (movement.StateCode == "Expired")
                return movement.ReservationExpiryReason == request.Reason.Trim()
                    ? ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Already expired.")
                    : ApiResponse<InventoryMovementDto>.ErrorResponse("Expired evidence differs from this request.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            if (movement.StateCode != "Reserved" || !movement.ReservationExpiresAtUtc.HasValue ||
                movement.ReservationExpiresAtUtc.Value > now || !Version(movement.RowVersion, request.RowVersion))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation is not due or has changed.", 409);
            movement.StateCode = "Expired";
            movement.ReservationExpiredAtUtc = now;
            movement.ReservationExpiredByUserId = user.UserId;
            movement.ReservationExpiryReason = request.Reason.Trim();
            movement.UpdatedAt = now;
            movement.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Remaining reservation expired; posted fulfilments remain intact.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Inventory expiry persistence conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Expiry conflict; reload.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960 or 51061)
        {
            logger.LogWarning(ex, "Inventory expiry SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Expiry conflict; reload.", 409);
        }
    }
}
