using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Dispatch is a posted Issue and receipt is a separately posted Receipt in the
/// existing canonical stock ledger. Stock remains in transit between timestamps.
/// </summary>
public sealed partial class InventoryMovementService
{
    public async Task<ApiResponse<InventoryMovementDto>> ReceiveTransferAsync(
        Guid dispatchReference, ReceiveInventoryTransferRequestDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (dispatchReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Dispatch and unique receipt request required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var dispatch = await db.InventoryMovements.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == dispatchReference && !x.IsDeleted, ct);
            if (dispatch == null || dispatch.SourceType != "TransferDispatch" ||
                dispatch.MovementTypeCode != "Issue" || dispatch.StateCode != "Posted" ||
                !dispatch.ToLocationId.HasValue ||
                !await LocationsAllowed(null, dispatch.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Transfer dispatch not found.", 404);

            var priorRequest = await db.InventoryMovements.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (priorRequest != null)
            {
                var sameReceipt = priorRequest.SourceType == "TransferReceipt" &&
                    priorRequest.SourceId == dispatch.Id && priorRequest.StateCode == "Posted" &&
                    !priorRequest.IsDeleted;
                var compensated = sameReceipt && await db.InventoryMovements.AsNoTracking()
                    .AnyAsync(x => x.TenantId == user.TenantId &&
                        x.ReversalOfMovementId == priorRequest.Id && x.StateCode == "Posted" &&
                        !x.IsDeleted, ct);
                return sameReceipt && !compensated
                    ? ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(priorRequest, ct), "Existing receipt returned.")
                    : ApiResponse<InventoryMovementDto>.ErrorResponse("Receipt request was corrected or belongs to another operation.", 409);
            }

            if (await db.InventoryMovements.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.ReversalOfMovementId == dispatch.Id &&
                x.StateCode == "Posted" && !x.IsDeleted, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Dispatch was cancelled.", 409);
            // Filtered SQL uniqueness protects distinct request IDs racing to receive one dispatch.
            if (await db.InventoryMovements.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.SourceType == "TransferReceipt" &&
                x.SourceId == dispatch.Id, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Transfer already received or corrected.", 409);

            var sourceLines = await db.InventoryMovementLines.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.InventoryMovementId == dispatch.Id && !x.IsDeleted)
                .OrderBy(x => x.LineNo).ToListAsync(ct);
            if (sourceLines.Count is < 1 or > 100)
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Dispatch lines require reconciliation.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var publicId = Guid.NewGuid();
            var receipt = new InventoryMovement
            {
                TenantId = user.TenantId, PublicId = publicId, ClientRequestId = request.ClientRequestId,
                MovementNumber = "IM-" + publicId.ToString("N").ToUpperInvariant(),
                MovementTypeCode = "Receipt", ToLocationId = dispatch.ToLocationId,
                SourceType = "TransferReceipt", SourceId = dispatch.Id,
                MovementAt = now, StateCode = "Posted", PostedAt = now, PostedByUserId = user.UserId,
                Remarks = "Received dispatch " + dispatch.MovementNumber,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.InventoryMovements.Add(receipt);
            await db.SaveChangesAsync(ct);
            var lines = sourceLines.Select(x => new InventoryMovementLine
            {
                TenantId = user.TenantId, InventoryMovementId = receipt.Id, LineNo = x.LineNo,
                InventoryItemId = x.InventoryItemId, Quantity = x.Quantity, UnitCost = x.UnitCost,
                LotOrBatchNo = x.LotOrBatchNo, Remarks = x.Remarks,
                CreatedAt = now, CreatedBy = user.UserId
            }).ToList();
            db.InventoryMovementLines.AddRange(lines);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(Map(receipt, lines), "Transfer received.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Inventory transfer receipt uniqueness or persistence conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Concurrent receipt; reload transfer.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        {
            logger.LogWarning(ex, "Inventory transfer receipt SQL concurrency conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Concurrent receipt; reload transfer.", 409);
        }
    }
}
