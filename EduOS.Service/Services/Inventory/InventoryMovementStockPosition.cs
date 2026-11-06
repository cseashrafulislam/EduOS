using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    // Read-only posted stock position. RecordedCostBalance is a net movement-cost
    // balance, not an approved FIFO/weighted-average financial valuation.
    public async Task<ApiResponse<InventoryStockPositionReportDto>> GetStockPositionAsync(
        long locationId, DateTime? asOfUtc, string? search, bool includeZeroStock,
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!Readable) return Denied<InventoryStockPositionReportDto>();
        if (locationId <= 0 || page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<InventoryStockPositionReportDto>.ErrorResponse("Valid location, page and page size (1-100) required.", 400);
        var term = search?.Trim();
        if (term?.Length > 100)
            return ApiResponse<InventoryStockPositionReportDto>.ErrorResponse("Search must not exceed 100 characters.", 400);
        var now = clock.GetUtcNow().UtcDateTime;
        var snapshot = asOfUtc ?? now;
        if (snapshot.Kind != DateTimeKind.Utc || snapshot > now)
            return ApiResponse<InventoryStockPositionReportDto>.ErrorResponse("As-of timestamp must be UTC and cannot be in the future.", 400);
        var location = await Locations().Where(x => x.Id == locationId)
            .Select(x => new { x.Id, x.Code, x.Name }).FirstOrDefaultAsync(ct);
        if (location == null) return Denied<InventoryStockPositionReportDto>();

        // Include inactive catalog items with historical stock; exclude untracked items.
        var query = db.InventoryItems.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.IsStockTracked);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        var catalogCount = await query.CountAsync(ct);
        // Aggregate posted movements in SQL, never materialize the matching catalog or
        // movement ledger. Validate every matching lot, including lots outside this page.
        var lots = Ledger(snapshot)
            .Where(x => x.LocationId == locationId && query.Any(i => i.Id == x.ItemId))
            .GroupBy(x => new { x.ItemId, x.Lot })
            .Select(g => new { g.Key.ItemId, Quantity = g.Sum(x => x.Quantity),
                Cost = g.Sum(x => x.Value) });
        if (await lots.AnyAsync(x => x.Quantity < 0m || x.Cost < 0m ||
            (x.Quantity == 0m && (x.Cost > 0.01m || x.Cost < -0.01m)), ct))
            return ApiResponse<InventoryStockPositionReportDto>.ErrorResponse(
                "Negative stock, negative recorded cost or residual lot cost requires reconciliation.", 409);

        var stock = lots.GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity),
                Cost = g.Sum(x => x.Cost), Lots = g.Count(x => x.Quantity != 0m) });
        var positions = from item in query
                        join balance in stock on item.Id equals balance.ItemId into grouped
                        from balance in grouped.DefaultIfEmpty()
                        select new {
                            item.Id, item.Code, item.Name, item.UnitCode, item.ReorderLevel, item.IsActive,
                            Quantity = balance == null ? 0m : balance.Quantity,
                            Cost = balance == null ? 0m : balance.Cost,
                            Lots = balance == null ? 0 : balance.Lots
                        };
        if (!includeZeroStock) positions = positions.Where(x => x.Quantity != 0m);
        var totalCount = includeZeroStock ? catalogCount : await positions.CountAsync(ct);
        var offset = ((long)page - 1L) * pageSize;
        var paged = offset > int.MaxValue ? new List<InventoryStockPositionRowDto>() :
            await positions.OrderBy(x => x.Code).ThenBy(x => x.Id)
                .Skip((int)offset).Take(pageSize)
                .Select(x => new InventoryStockPositionRowDto {
                    InventoryItemId = x.Id, ItemCode = x.Code, ItemName = x.Name,
                    UnitCode = x.UnitCode, ReorderLevel = x.ReorderLevel,
                    QuantityOnHand = x.Quantity, RecordedCostBalance = x.Cost,
                    ActiveLotCount = x.Lots, IsActive = x.IsActive,
                    IsBelowReorder = x.ReorderLevel > 0m && x.Quantity <= x.ReorderLevel
                }).ToListAsync(ct);
        return ApiResponse<InventoryStockPositionReportDto>.SuccessResponse(new InventoryStockPositionReportDto
        {
            InventoryLocationId = location.Id, LocationName = location.Code + " — " + location.Name,
            AsOfUtc = snapshot, Search = term, IncludeZeroStock = includeZeroStock,
            CatalogItemCount = catalogCount, Page = page, PageSize = pageSize,
            TotalCount = totalCount, Items = paged
        });
    }
}
