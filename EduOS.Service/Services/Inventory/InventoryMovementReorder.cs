using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    // ReorderLevel belongs to the canonical item catalog. It is applied independently
    // at the requested, authorized location; it is not a purchase order or reservation.
    public async Task<ApiResponse<InventoryReorderReportDto>> GetReorderAlertsAsync(long locationId, CancellationToken ct = default)
    {
        if (!Readable) return Denied<InventoryReorderReportDto>();
        if (locationId <= 0)
            return ApiResponse<InventoryReorderReportDto>.ErrorResponse("A stock location is required.", 400);
        var location = await Locations().Where(x => x.Id == locationId)
            .Select(x => new { x.Id, x.Name, x.Code }).FirstOrDefaultAsync(ct);
        if (location == null) return Denied<InventoryReorderReportDto>();
        var query = db.InventoryItems.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.IsActive && x.IsStockTracked && x.ReorderLevel > 0m);
        if (await query.CountAsync(ct) > 2000)
            return ApiResponse<InventoryReorderReportDto>.ErrorResponse("More than 2,000 reorder-tracked items; filtered reporting is required.", 422);
        var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Code, x.Name, x.UnitCode, x.ReorderLevel }).ToListAsync(ct);
        var asOf = clock.GetUtcNow().UtcDateTime;
        var quantities = new Dictionary<long, decimal>();
        // Chunk SQL parameters; reuse the same posted/reversal-aware stock ledger as stock balance.
        foreach (var batch in items.Chunk(200))
        {
            var values = await Balances(batch.Select(x => x.Id).ToArray(), new[] { locationId }, asOf, ct);
            foreach (var value in values.Values)
            {
                quantities.TryGetValue(value.ItemId, out var existing);
                quantities[value.ItemId] = existing + value.Quantity;
            }
        }
        if (quantities.Values.Any(x => x < 0m))
            return ApiResponse<InventoryReorderReportDto>.ErrorResponse("Negative posted stock requires reconciliation before reorder reporting.", 409);
        var alerts = items.Select(x =>
        {
            quantities.TryGetValue(x.Id, out var onHand);
            return new InventoryReorderRowDto
            {
                InventoryItemId = x.Id, ItemCode = x.Code, ItemName = x.Name,
                UnitCode = x.UnitCode, ReorderLevel = x.ReorderLevel,
                QuantityOnHand = onHand, Shortfall = Math.Max(0m, x.ReorderLevel - onHand)
            };
        }).Where(x => x.QuantityOnHand <= x.ReorderLevel).ToArray();
        return ApiResponse<InventoryReorderReportDto>.SuccessResponse(new InventoryReorderReportDto
        {
            InventoryLocationId = location.Id, LocationName = location.Code + " — " + location.Name,
            AsOfUtc = asOf, MonitoredItemCount = items.Count, Alerts = alerts
        });
    }
}
