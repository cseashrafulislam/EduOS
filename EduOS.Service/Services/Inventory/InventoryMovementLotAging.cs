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
    /// Current posted lot balances and the last inbound event at this location.
    /// Last inbound is a dormancy indicator, not the acquisition date of remaining
    /// units. Cost is the recorded net movement-cost balance, not FIFO/GL valuation.
    /// No separate stock ledger or cached balance is maintained.
    /// </summary>
    public async Task<ApiResponse<PagedResult<InventoryLotAgingRowDto>>> GetLotAgingAsync(
        long locationId, string? search, int minimumDays, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId)
            return Denied<PagedResult<InventoryLotAgingRowDto>>();
        if (locationId <= 0 || minimumDays is < 0 or > 3650 ||
            page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<PagedResult<InventoryLotAgingRowDto>>.ErrorResponse(
                "Valid store, minimum days (0-3650), page and page size (1-100) required.", 400);
        var term = search?.Trim();
        if (term?.Length > 100)
            return ApiResponse<PagedResult<InventoryLotAgingRowDto>>.ErrorResponse(
                "Search exceeds 100 characters.", 400);
        if (!await Locations().AnyAsync(x => x.Id == locationId, ct))
            return ApiResponse<PagedResult<InventoryLotAgingRowDto>>.ErrorResponse(
                "Store not found.", 404);
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            // Read the count, page, posted ledger and live holds in one serializable
            // transaction so a concurrent issue/receipt cannot split the report.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var balances = Ledger(null).Where(x => x.LocationId == locationId)
                .GroupBy(x => new { x.ItemId, x.Lot })
                .Select(g => new
                {
                    g.Key.ItemId, g.Key.Lot,
                    Quantity = g.Sum(x => x.Quantity),
                    RecordedCost = g.Sum(x => x.Value)
                })
                .Where(x => x.Quantity != 0m || x.RecordedCost != 0m);

            var inbound =
                from line in db.InventoryMovementLines.AsNoTracking()
                join movement in db.InventoryMovements.AsNoTracking()
                    on line.InventoryMovementId equals movement.Id
                where line.TenantId == user.TenantId && movement.TenantId == user.TenantId &&
                    !line.IsDeleted && !movement.IsDeleted &&
                    movement.StateCode == "Posted" && movement.PostedAt.HasValue &&
                    (((movement.MovementTypeCode == "Receipt" ||
                       movement.MovementTypeCode == "Transfer") && movement.ToLocationId == locationId) ||
                     ((movement.MovementTypeCode == "ReversalIssue" ||
                       movement.MovementTypeCode == "ReversalTransfer") && movement.FromLocationId == locationId))
                group movement by new
                {
                    ItemId = line.InventoryItemId,
                    Lot = line.LotOrBatchNo ?? ""
                } into g
                select new
                {
                    g.Key.ItemId, g.Key.Lot,
                    LatestInbound = g.Max(x => x.PostedAt)
                };

            var query =
                from balance in balances
                join item in db.InventoryItems.AsNoTracking()
                    on balance.ItemId equals item.Id
                join last in inbound on new { balance.ItemId, balance.Lot }
                    equals new { last.ItemId, last.Lot } into inboundJoin
                from last in inboundJoin.DefaultIfEmpty()
                where item.TenantId == user.TenantId && !item.IsDeleted && item.IsStockTracked
                select new
                {
                    balance.ItemId, balance.Lot, balance.Quantity, balance.RecordedCost,
                    ItemCode = item.Code, ItemName = item.Name, item.UnitCode,
                    LatestInbound = last == null ? (DateTime?)null : last.LatestInbound
                };
            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(x => x.ItemCode.Contains(term) ||
                    x.ItemName.Contains(term) || x.Lot.Contains(term));
            if (minimumDays > 0)
            {
                var cutoff = now.AddDays(-minimumDays);
                query = query.Where(x => !x.LatestInbound.HasValue ||
                    x.LatestInbound.Value <= cutoff);
            }

            var count = await query.CountAsync(ct);
            var offset = ((long)page - 1L) * pageSize;
            var selected = offset > int.MaxValue
                ? new List<InventoryLotAgingRowDto>()
                : await query.OrderBy(x => x.ItemCode).ThenBy(x => x.ItemId)
                    .ThenBy(x => x.Lot).Skip((int)offset).Take(pageSize)
                    .Select(x => new InventoryLotAgingRowDto
                    {
                        InventoryItemId = x.ItemId, ItemCode = x.ItemCode,
                        ItemName = x.ItemName, UnitCode = x.UnitCode,
                        LotOrBatchNo = x.Lot, QuantityOnHand = x.Quantity,
                        RecordedCostBalance = x.RecordedCost,
                        LatestInboundUtc = x.LatestInbound
                    }).ToListAsync(ct);

            if (selected.Count > 0)
            {
                var items = selected.Select(x => x.InventoryItemId).Distinct().ToArray();
                var holds = await ReservedQuantitiesAsync(items, new[] { locationId }, null, ct);
                foreach (var row in selected)
                {
                    row.ReservedQuantity = holds.GetValueOrDefault(
                        (row.InventoryItemId, locationId, row.LotOrBatchNo));
                    row.AvailableQuantity = row.QuantityOnHand - row.ReservedQuantity;
                    if (row.LatestInboundUtc.HasValue)
                    {
                        var utc = DateTime.SpecifyKind(row.LatestInboundUtc.Value, DateTimeKind.Utc);
                        row.LatestInboundUtc = utc;
                        row.DaysSinceLatestInbound = Math.Max(0,
                            (int)Math.Floor((now - utc).TotalDays));
                    }
                    row.HasIntegrityException = row.QuantityOnHand < 0m ||
                        row.AvailableQuantity < 0m ||
                        (row.QuantityOnHand == 0m && Math.Abs(row.RecordedCostBalance) > 0.01m) ||
                        (row.QuantityOnHand > 0m && !row.LatestInboundUtc.HasValue);
                    row.StatusCode = row.QuantityOnHand < 0m ? "NegativeStock" :
                        row.AvailableQuantity < 0m ? "ReservationShortfall" :
                        row.QuantityOnHand == 0m ? "ResidualCost" :
                        !row.LatestInboundUtc.HasValue ? "MissingInbound" :
                        row.DaysSinceLatestInbound >= 90 ? "Dormant" : "Current";
                    row.AsOfUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);
                }
            }
            await tx.CommitAsync(ct);
            return ApiResponse<PagedResult<InventoryLotAgingRowDto>>.SuccessResponse(
                new PagedResult<InventoryLotAgingRowDto>
                {
                    Items = selected, TotalCount = count, Page = page, PageSize = pageSize
                });
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 3960)
        {
            logger.LogWarning(ex, "Inventory lot aging SQL contention");
            return ApiResponse<PagedResult<InventoryLotAgingRowDto>>.ErrorResponse(
                "Stock changed during aging reconciliation; retry.", 409);
        }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        {
            logger.LogWarning(ex, "Inventory lot aging wrapped SQL contention");
            return ApiResponse<PagedResult<InventoryLotAgingRowDto>>.ErrorResponse(
                "Stock changed during aging reconciliation; retry.", 409);
        }
    }
}
