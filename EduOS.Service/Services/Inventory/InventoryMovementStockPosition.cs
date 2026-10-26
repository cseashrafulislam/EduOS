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
        if (catalogCount > 2000)
            return ApiResponse<InventoryStockPositionReportDto>.ErrorResponse(
                "More than 2,000 matching stock items; narrow the item search.", 422);
        var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Code, x.Name, x.UnitCode, x.ReorderLevel, x.IsActive })
            .ToListAsync(ct);

        var balances = new Dictionary<long, (decimal Quantity, decimal Cost, int Lots)>();
        foreach (var batch in items.Chunk(200))
        {
            var lots = await Balances(batch.Select(x => x.Id).ToArray(), new[] { locationId }, snapshot, ct);
            if (!Nonnegative(lots.Values))
                return ApiResponse<InventoryStockPositionReportDto>.ErrorResponse(
                    "Negative stock, negative recorded cost or residual lot cost requires reconciliation.", 409);
            foreach (var lot in lots.Values)
            {
                balances.TryGetValue(lot.ItemId, out var current);
                balances[lot.ItemId] = (current.Quantity + lot.Quantity,
                    current.Cost + lot.Value, current.Lots + (lot.Quantity != 0m ? 1 : 0));
            }
        }

        var rows = new List<InventoryStockPositionRowDto>(items.Count);
        foreach (var item in items)
        {
            balances.TryGetValue(item.Id, out var balance);
            if (!includeZeroStock && balance.Quantity == 0m) continue;
            rows.Add(new InventoryStockPositionRowDto
            {
                InventoryItemId = item.Id, ItemCode = item.Code, ItemName = item.Name,
                UnitCode = item.UnitCode, ReorderLevel = item.ReorderLevel,
                QuantityOnHand = balance.Quantity, RecordedCostBalance = balance.Cost,
                ActiveLotCount = balance.Lots, IsActive = item.IsActive,
                IsBelowReorder = item.ReorderLevel > 0m && balance.Quantity <= item.ReorderLevel
            });
        }
        var offset = ((long)page - 1L) * pageSize;
        var paged = offset > int.MaxValue ? new List<InventoryStockPositionRowDto>() :
            rows.Skip((int)offset).Take(pageSize).ToList();
        return ApiResponse<InventoryStockPositionReportDto>.SuccessResponse(new InventoryStockPositionReportDto
        {
            InventoryLocationId = location.Id, LocationName = location.Code + " — " + location.Name,
            AsOfUtc = snapshot, Search = term, IncludeZeroStock = includeZeroStock,
            CatalogItemCount = catalogCount, Page = page, PageSize = pageSize,
            TotalCount = rows.Count, Items = paged
        });
    }
}
