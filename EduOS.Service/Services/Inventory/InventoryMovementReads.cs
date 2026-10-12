using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    public async Task<ApiResponse<PagedResult<InventoryMovementDto>>> GetMovementsAsync(DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Readable) return Denied<PagedResult<InventoryMovementDto>>();
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc)
            return ApiResponse<PagedResult<InventoryMovementDto>>.ErrorResponse("Invalid date range.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Visible();
        if (fromUtc.HasValue) query = query.Where(x => x.MovementAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.MovementAt <= toUtc.Value);
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var movements = offset > int.MaxValue ? new List<InventoryMovement>() :
            await query.OrderByDescending(x => x.MovementAt).ThenByDescending(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var ids = movements.Select(x => x.Id).ToArray();
        var lines = await db.InventoryMovementLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            ids.Contains(x.InventoryMovementId) && !x.IsDeleted).ToListAsync(ct);
        var reversed = (await db.InventoryMovements.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.ReversalOfMovementId.HasValue && ids.Contains(x.ReversalOfMovementId.Value) &&
            x.StateCode == "Posted" && !x.IsDeleted).Select(x => x.ReversalOfMovementId!.Value).ToListAsync(ct)).ToHashSet();
        return ApiResponse<PagedResult<InventoryMovementDto>>.SuccessResponse(new PagedResult<InventoryMovementDto> {
            Items = movements.Select(x => Map(x, lines.Where(l => l.InventoryMovementId == x.Id), reversed.Contains(x.Id))).ToList(),
            Page = page, PageSize = pageSize, TotalCount = total
        });
    }
    public async Task<ApiResponse<InventoryMovementDto>> GetMovementAsync(Guid movementReference, CancellationToken ct = default)
    {
        if (!Readable) return Denied<InventoryMovementDto>();
        var movement = await Visible().FirstOrDefaultAsync(x => x.PublicId == movementReference, ct);
        return movement == null ? ApiResponse<InventoryMovementDto>.ErrorResponse("Movement not found.", 404) :
            ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct));
    }
    public async Task<ApiResponse<InventoryBalanceDto>> GetItemBalanceAsync(long itemId, long locationId, DateTime? asOfUtc, CancellationToken ct = default)
    {
        if (!Readable) return Denied<InventoryBalanceDto>();
        if (itemId <= 0 || locationId <= 0) return ApiResponse<InventoryBalanceDto>.ErrorResponse("Item and location required.");
        if (!await Locations().AnyAsync(x => x.Id == locationId, ct)) return Denied<InventoryBalanceDto>();
        if (!await db.InventoryItems.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId && x.Id == itemId && !x.IsDeleted, ct))
            return ApiResponse<InventoryBalanceDto>.ErrorResponse("Item not found.", 404);
        var rows = await Balances(new[] { itemId }, new[] { locationId }, asOfUtc, ct);
        return ApiResponse<InventoryBalanceDto>.SuccessResponse(new InventoryBalanceDto {
            InventoryItemId = itemId, InventoryLocationId = locationId,
            QuantityOnHand = rows.Values.Sum(x => x.Quantity), ValuationAmount = rows.Values.Sum(x => x.Value),
            AsOfUtc = asOfUtc
        });
    }
}