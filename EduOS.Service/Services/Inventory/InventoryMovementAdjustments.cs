using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    /// <summary>
    /// A manual adjustment is a single-line canonical Receipt/Issue draft.
    /// No stock is changed until a different authorized checker posts it.
    /// All competing reservations and stock invariants are rechecked on posting.
    /// </summary>
    public async Task<ApiResponse<InventoryMovementDto>> CreateStockAdjustmentAsync(
        CreateInventoryStockAdjustmentRequestDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.LocationId <= 0 || request.InventoryItemId <= 0 ||
            request.Direction is not ("Increase" or "Decrease") ||
            request.Quantity <= 0m || request.Quantity > 999999999999m ||
            !Precision(request.Quantity, 6) ||
            request.UnitCost < 0m || request.UnitCost > 999999999999m ||
            !Precision(request.UnitCost, 4) ||
            (request.Direction == "Increase" && request.UnitCost <= 0m) ||
            (request.Direction == "Decrease" && request.UnitCost != 0m) ||
            string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 10 or > 800 ||
            request.LotOrBatchNo?.Trim().Length > 100)
            return ApiResponse<InventoryMovementDto>.ErrorResponse(
                "Valid direction, item, location, quantity, receipt cost and reason (10-800 characters) required.", 400);
        if (!await LocationsAllowed(request.LocationId, null, ct))
            return Denied<InventoryMovementDto>();
        if (!await db.InventoryItems.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.InventoryItemId &&
                x.IsStockTracked && x.IsActive && !x.IsDeleted, ct))
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Stock item not found.", 404);

        var increase = request.Direction == "Increase";
        var draft = new CreateInventoryMovementRequestDto
        {
            ClientRequestId = request.ClientRequestId,
            MovementTypeCode = increase ? "Receipt" : "Issue",
            FromLocationId = increase ? null : request.LocationId,
            ToLocationId = increase ? request.LocationId : null,
            SourceType = "ManualAdjustment",
            Remarks = "Stock adjustment: " + request.Reason.Trim(),
            Lines = [new CreateInventoryMovementLineRequestDto
            {
                InventoryItemId = request.InventoryItemId,
                Quantity = request.Quantity,
                UnitCost = increase ? request.UnitCost : 0m,
                LotOrBatchNo = request.LotOrBatchNo?.Trim() ?? ""
            }]
        };
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // Authorization and catalog state are rechecked under the stock transaction.
            if (!await LocationsAllowed(request.LocationId, null, ct))
                return Denied<InventoryMovementDto>();
            if (!await db.InventoryItems.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == request.InventoryItemId &&
                    x.IsStockTracked && x.IsActive && !x.IsDeleted, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Stock item not found.", 404);
            var existing = await ExistingCreateAsync(draft, ct);
            if (existing != null) return existing;
            var (movement, lines) = await PersistDraftAsync(draft, ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(
                Map(movement, lines), "Adjustment submitted for independent approval.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Inventory adjustment create conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Adjustment conflict; reload.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        {
            logger.LogWarning(ex, "Inventory adjustment SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Adjustment conflict; reload.", 409);
        }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        {
            logger.LogWarning(ex, "Inventory adjustment wrapped SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Adjustment conflict; reload.", 409);
        }
    }
}
