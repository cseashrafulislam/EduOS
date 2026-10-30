using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// A Reserved issue/transfer draft holds physical stock without posting to the
/// canonical movement ledger. Only Posted movements affect on-hand valuation.
/// Serializable transactions protect reservations and all competing stock writes.
/// </summary>
public sealed partial class InventoryMovementService
{
    private async Task<Dictionary<(long Item, long Location, string Lot), decimal>> ReservedQuantitiesAsync(
        long[] items, long[] locations, long? excludeMovementId, CancellationToken ct)
    {
        if (items.Length == 0 || locations.Length == 0)
            return new Dictionary<(long, long, string), decimal>();
        var rows = await (
            from line in db.InventoryMovementLines.AsNoTracking()
            join movement in db.InventoryMovements.AsNoTracking()
                on line.InventoryMovementId equals movement.Id
            where line.TenantId == user.TenantId && movement.TenantId == user.TenantId &&
                  !line.IsDeleted && !movement.IsDeleted &&
                  movement.StateCode == "Reserved" && movement.FromLocationId.HasValue &&
                  locations.Contains(movement.FromLocationId.Value) &&
                  items.Contains(line.InventoryItemId) &&
                  (!excludeMovementId.HasValue || movement.Id != excludeMovementId.Value)
            group line by new
            {
                Item = line.InventoryItemId,
                Location = movement.FromLocationId!.Value,
                Lot = line.LotOrBatchNo ?? ""
            } into groupRows
            select new
            {
                groupRows.Key.Item, groupRows.Key.Location, groupRows.Key.Lot,
                Quantity = groupRows.Sum(x => x.Quantity - x.FulfilledQuantity)
            }).ToListAsync(ct);
        return rows.ToDictionary(x => (x.Item, x.Location, x.Lot), x => x.Quantity);
    }

    private async Task<(string? Error, List<InventoryMovementLine> Lines)> CheckReservationAsync(
        InventoryMovement movement, CancellationToken ct)
    {
        var lines = await db.InventoryMovementLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.InventoryMovementId == movement.Id && !x.IsDeleted)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        if (lines.Count is < 1 or > 100 || !movement.FromLocationId.HasValue)
            return ("Invalid reservation lines or origin.", lines);
        var items = lines.Select(x => x.InventoryItemId).Distinct().ToArray();
        if (await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                items.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != items.Length)
            return ("Inactive or missing stock item.", lines);
        var location = movement.FromLocationId.Value;
        var balances = await Balances(items, new[] { location }, null, ct);
        if (!Nonnegative(balances.Values))
            return ("Stock ledger requires reconciliation.", lines);
        var reserved = await ReservedQuantitiesAsync(items, new[] { location }, movement.Id, ct);
        foreach (var group in lines.GroupBy(x => (x.InventoryItemId, Lot: x.LotOrBatchNo ?? "")))
        {
            var key = (group.Key.InventoryItemId, location, group.Key.Lot);
            var available = Get(balances, group.Key.InventoryItemId, location, group.Key.Lot).Quantity -
                reserved.GetValueOrDefault(key);
            if (available < group.Sum(x => x.Quantity))
                return ("Insufficient unreserved item/lot stock.", lines);
        }
        return (null, lines);
    }

    public async Task<ApiResponse<InventoryMovementDto>> ReserveMovementAsync(
        Guid movementReference, PostInventoryMovementRequestDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (movementReference == Guid.Empty || request == null)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement and row version required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var movement = await db.InventoryMovements.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == movementReference && !x.IsDeleted, ct);
            if (movement == null || !await LocationsAllowed(movement.FromLocationId, movement.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement not found.", 404);
            if (movement.StateCode == "Reserved")
                return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Already reserved.");
            if (movement.StateCode != "Draft" ||
                movement.MovementTypeCode is not ("Issue" or "Transfer") ||
                !Version(movement.RowVersion, request.RowVersion))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement changed or cannot be reserved.", 409);
            var (error, lines) = await CheckReservationAsync(movement, ct);
            if (error != null) return ApiResponse<InventoryMovementDto>.ErrorResponse(error, 409);
            var now = clock.GetUtcNow().UtcDateTime;
            movement.StateCode = "Reserved";
            movement.ReservationHeldAtUtc = now;
            movement.UpdatedAt = now;
            movement.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(Map(movement, lines), "Stock reserved.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Inventory reservation persistence conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation conflict; reload.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        {
            logger.LogWarning(ex, "Inventory reservation SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation conflict; reload.", 409);
        }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        {
            logger.LogWarning(ex, "Inventory reservation wrapped SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation conflict; reload.", 409);
        }
    }

    public async Task<ApiResponse<InventoryMovementDto>> ReleaseReservationAsync(
        Guid movementReference, ReleaseInventoryReservationRequestDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (movementReference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 400)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement and release reason (10-400 characters) required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var movement = await db.InventoryMovements.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == movementReference && !x.IsDeleted, ct);
            if (movement == null || !await LocationsAllowed(movement.FromLocationId, movement.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement not found.", 404);
            if (!Postable && movement.CreatedBy != user.UserId)
                return Denied<InventoryMovementDto>();
            if (movement.StateCode == "Cancelled")
                return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Already released.");
            if (movement.StateCode != "Reserved" || !Version(movement.RowVersion, request.RowVersion))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation changed or cannot be released.", 409);
            var remarks = (movement.Remarks ?? "") + "\nReservation released: " + request.Reason.Trim();
            if (remarks.Length > 1000)
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Release reason exceeds available audit remarks length.", 422);
            var now = clock.GetUtcNow().UtcDateTime;
            movement.Remarks = remarks;
            movement.StateCode = "Cancelled";
            movement.UpdatedAt = now;
            movement.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Reservation released.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Inventory reservation release conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Release conflict; reload.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        {
            logger.LogWarning(ex, "Inventory reservation release SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Release conflict; reload.", 409);
        }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        {
            logger.LogWarning(ex, "Inventory reservation release wrapped SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Release conflict; reload.", 409);
        }
    }
}
