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

        // A separate item-level total filters nonzero stock before paging. Project
        // catalog rows without a nullable aggregate left join (portable across
        // SQL Server and EF InMemory); fetch balances only for the chosen page.
        var positions = query;
        if (!includeZeroStock)
        {
            var stockedItemIds = Ledger(snapshot).Where(x => x.LocationId == locationId)
                .GroupBy(x => x.ItemId).Where(g => g.Sum(x => x.Quantity) != 0m)
                .Select(g => g.Key);
            positions = positions.Where(x => stockedItemIds.Contains(x.Id));
        }
        var totalCount = includeZeroStock ? catalogCount : await positions.CountAsync(ct);
        var offset = ((long)page - 1L) * pageSize;
        var itemPage = positions.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Code, x.Name, x.UnitCode, x.ReorderLevel, x.IsActive });
        itemPage = offset > int.MaxValue ? itemPage.Take(0) : itemPage.Skip((int)offset).Take(pageSize);
        var items = await itemPage.ToListAsync(ct);
        var balances = items.Count == 0
            ? new Dictionary<(long, long, string), Balance>()
            : await Balances(items.Select(x => x.Id).ToArray(), new[] { locationId }, snapshot, ct);
        if (!Nonnegative(balances.Values))
            return ApiResponse<InventoryStockPositionReportDto>.ErrorResponse(
                "Negative stock, negative recorded cost or residual lot cost requires reconciliation.", 409);
        var pageStock = balances.Values.GroupBy(x => x.ItemId).ToDictionary(x => x.Key,
            x => (Quantity: x.Sum(v => v.Quantity), Cost: x.Sum(v => v.Value),
                Lots: x.Count(v => v.Quantity != 0m)));
        var paged = items.Select(x => {
            pageStock.TryGetValue(x.Id, out var stock);
            return new InventoryStockPositionRowDto {
                InventoryItemId = x.Id, ItemCode = x.Code, ItemName = x.Name,
                UnitCode = x.UnitCode, ReorderLevel = x.ReorderLevel,
                QuantityOnHand = stock.Quantity, RecordedCostBalance = stock.Cost,
                ActiveLotCount = stock.Lots, IsActive = x.IsActive,
                IsBelowReorder = x.ReorderLevel > 0m && stock.Quantity <= x.ReorderLevel
            };
        }).ToList();
        return ApiResponse<InventoryStockPositionReportDto>.SuccessResponse(new InventoryStockPositionReportDto
        {
            InventoryLocationId = location.Id, LocationName = location.Code + " — " + location.Name,
            AsOfUtc = snapshot, Search = term, IncludeZeroStock = includeZeroStock,
            CatalogItemCount = catalogCount, Page = page, PageSize = pageSize,
            TotalCount = totalCount, Items = paged
        });
    }
}
