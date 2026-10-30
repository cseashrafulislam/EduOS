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
    /// Operational reconciliation of active holds against the one posted movement ledger.
    /// One row per item/lot at an authorized location, not a second stock balance table.
    /// All quantities are current, not reconstructed historical snapshots.
    /// </summary>
    public async Task<ApiResponse<PagedResult<InventoryReservationCoverageRowDto>>> GetReservationCoverageAsync(
        long locationId, string? search, bool overdueOnly, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId)
            return Denied<PagedResult<InventoryReservationCoverageRowDto>>();
        if (locationId <= 0 || page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<PagedResult<InventoryReservationCoverageRowDto>>.ErrorResponse(
                "Authorized store, page and page size (1-100) required.", 400);
        var term = search?.Trim();
        if (term?.Length > 100)
            return ApiResponse<PagedResult<InventoryReservationCoverageRowDto>>.ErrorResponse(
                "Search exceeds 100 characters.", 400);
        if (!await Locations().AnyAsync(x => x.Id == locationId, ct))
            return ApiResponse<PagedResult<InventoryReservationCoverageRowDto>>.ErrorResponse(
                "Store not found.", 404);

        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            // The count, holds and posted-stock projection must not straddle a
            // concurrent dispatch/release. Writers use SERIALIZABLE as well.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var holds =
                from line in db.InventoryMovementLines.AsNoTracking()
                join movement in Visible() on line.InventoryMovementId equals movement.Id
                where line.TenantId == user.TenantId && !line.IsDeleted &&
                    movement.StateCode == "Reserved" &&
                    movement.FromLocationId == locationId &&
                    (movement.MovementTypeCode == "Issue" || movement.MovementTypeCode == "Transfer") &&
                    line.Quantity > line.FulfilledQuantity
                group new { line, movement } by new
                {
                    line.InventoryItemId,
                    Lot = line.LotOrBatchNo ?? ""
                } into groupRows
                select new
                {
                    groupRows.Key.InventoryItemId,
                    groupRows.Key.Lot,
                    Reserved = groupRows.Sum(x => x.line.Quantity - x.line.FulfilledQuantity),
                    Overdue = groupRows.Sum(x =>
                        x.movement.ReservationExpiresAtUtc.HasValue &&
                        x.movement.ReservationExpiresAtUtc.Value <= now
                            ? x.line.Quantity - x.line.FulfilledQuantity : 0m),
                    HoldLines = groupRows.Count(),
                    Oldest = groupRows.Min(x =>
                        x.movement.ReservationHeldAtUtc ?? x.movement.CreatedAt)
                };

            var query =
                from hold in holds
                join item in db.InventoryItems.AsNoTracking()
                    on hold.InventoryItemId equals item.Id
                where item.TenantId == user.TenantId && !item.IsDeleted &&
                    item.IsStockTracked && hold.Reserved > 0m
                select new
                {
                    hold.InventoryItemId, hold.Lot, hold.Reserved, hold.Overdue,
                    hold.HoldLines, hold.Oldest,
                    ItemCode = item.Code, ItemName = item.Name, item.UnitCode
                };
            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(x => x.ItemCode.Contains(term) ||
                    x.ItemName.Contains(term) || x.Lot.Contains(term));
            if (overdueOnly)
                query = query.Where(x => x.Overdue > 0m);

            var total = await query.CountAsync(ct);
            var offset = ((long)page - 1) * pageSize;
            var selected = offset > int.MaxValue
                ? new List<InventoryReservationCoverageRowDto>()
                : await query.OrderBy(x => x.ItemCode).ThenBy(x => x.InventoryItemId)
                    .ThenBy(x => x.Lot).Skip((int)offset).Take(pageSize)
                    .Select(x => new InventoryReservationCoverageRowDto
                    {
                        InventoryItemId = x.InventoryItemId,
                        ItemCode = x.ItemCode, ItemName = x.ItemName,
                        UnitCode = x.UnitCode, LotOrBatchNo = x.Lot,
                        ReservedQuantity = x.Reserved,
                        OverdueReservedQuantity = x.Overdue,
                        ActiveHoldLines = x.HoldLines,
                        OldestHoldUtc = x.Oldest
                    }).ToListAsync(ct);

            if (selected.Count > 0)
            {
                var items = selected.Select(x => x.InventoryItemId).Distinct().ToArray();
                var lots = selected.Select(x => x.LotOrBatchNo).Distinct().ToArray();
                var posted = await Ledger(null)
                    .Where(x => x.LocationId == locationId &&
                        items.Contains(x.ItemId) && lots.Contains(x.Lot))
                    .GroupBy(x => new { x.ItemId, x.Lot })
                    .Select(x => new
                    {
                        x.Key.ItemId, x.Key.Lot,
                        Quantity = x.Sum(y => y.Quantity)
                    }).ToListAsync(ct);
                var stock = posted.ToDictionary(x => (x.ItemId, x.Lot), x => x.Quantity);
                foreach (var row in selected)
                {
                    row.QuantityOnHand = stock.GetValueOrDefault(
                        (row.InventoryItemId, row.LotOrBatchNo));
                    row.AvailableQuantity = row.QuantityOnHand - row.ReservedQuantity;
                    row.ShortfallQuantity = Math.Max(0m, -row.AvailableQuantity);
                    row.HasShortfall = row.ShortfallQuantity > 0m;
                    row.IsOverdue = row.OverdueReservedQuantity > 0m;
                    row.StatusCode = row.HasShortfall ? "Shortfall" :
                        row.IsOverdue ? "Overdue" : "Covered";
                    row.OldestHoldUtc = DateTime.SpecifyKind(
                        row.OldestHoldUtc, DateTimeKind.Utc);
                    row.OldestHoldAgeDays = Math.Max(0,
                        (int)Math.Floor((now - row.OldestHoldUtc).TotalDays));
                    row.AsOfUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);
                }
            }
            await tx.CommitAsync(ct);
            return ApiResponse<PagedResult<InventoryReservationCoverageRowDto>>.SuccessResponse(
                new PagedResult<InventoryReservationCoverageRowDto>
                {
                    Items = selected, TotalCount = total, Page = page, PageSize = pageSize
                });
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 3960)
        {
            logger.LogWarning(ex, "Inventory reservation coverage SQL contention");
            return ApiResponse<PagedResult<InventoryReservationCoverageRowDto>>.ErrorResponse(
                "Stock changed during reconciliation; retry.", 409);
        }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        {
            logger.LogWarning(ex, "Inventory reservation coverage wrapped SQL contention");
            return ApiResponse<PagedResult<InventoryReservationCoverageRowDto>>.ErrorResponse(
                "Stock changed during reconciliation; retry.", 409);
        }
    }
}
