using Microsoft.Extensions.Logging;
using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    public async Task<ApiResponse<InventoryMovementDto>> ReverseMovementAsync(Guid movementReference, ReverseInventoryMovementRequestDto request, CancellationToken ct = default)
    {
        if (!Reversible) return Denied<InventoryMovementDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 10 or > 1000)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Unique request ID and reason (10-1000 characters) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var original = await db.InventoryMovements.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == movementReference && !x.IsDeleted, ct);
            if (original == null || !await LocationsAllowed(original.FromLocationId, original.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement not found.", 404);
            var conflict = await ReversalConflictAsync(original, request, ct);
            if (conflict != null) return conflict;
            var (error, lines) = await CheckReversalAsync(original, ct);
            if (error != null) return ApiResponse<InventoryMovementDto>.ErrorResponse(error, 409);
            var (reversal, copies) = await PersistReversalAsync(original, lines, request.ClientRequestId, request.Reason.Trim(), ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(Map(reversal, copies), "Compensating stock movement posted.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Inventory reversal conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Reversal conflict; reload.", 409); }
    }
}