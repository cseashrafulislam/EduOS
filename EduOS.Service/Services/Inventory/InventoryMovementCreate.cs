using Microsoft.Extensions.Logging;
using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    public async Task<ApiResponse<InventoryMovementDto>> CreateMovementAsync(CreateInventoryMovementRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Denied<InventoryMovementDto>();
        var error = ValidateCreate(request);
        if (error != null) return ApiResponse<InventoryMovementDto>.ErrorResponse(error);
        if (!await LocationsAllowed(request.FromLocationId, request.ToLocationId, ct)) return Denied<InventoryMovementDto>();
        var ids = request.Lines.Select(x => x.InventoryItemId).Distinct().ToArray();
        if (await db.InventoryItems.CountAsync(x => x.TenantId == user.TenantId && ids.Contains(x.Id) &&
            x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != ids.Length)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Invalid stock item.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await ExistingCreateAsync(request, ct);
            if (previous != null) return previous;
            var (movement, lines) = await PersistDraftAsync(request, ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(Map(movement, lines), "Draft created.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Inventory draft conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Concurrent change; reload.", 409); }
    }
}