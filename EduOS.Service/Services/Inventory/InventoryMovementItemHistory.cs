using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    // Uses the same posted movement/reversal direction rules as Ledger().
    // Does not infer FIFO, weighted-average valuation or supplier invoice provenance.
    public async Task<ApiResponse<InventoryItemHistoryReportDto>> GetItemHistoryAsync(
        long itemId, long locationId, DateTime? fromUtc, DateTime? toUtc,
        string? lot, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId)
            return Denied<InventoryItemHistoryReportDto>();
        if (itemId <= 0 || locationId <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue ||
            (fromUtc.HasValue && fromUtc.Value.Kind != DateTimeKind.Utc) ||
            (toUtc.HasValue && toUtc.Value.Kind != DateTimeKind.Utc) ||
            (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value))
            return ApiResponse<InventoryItemHistoryReportDto>.ErrorResponse("Invalid item, location, UTC date range or pagination.", 400);

        var lotFilter = string.IsNullOrWhiteSpace(lot) ? null : lot.Trim();
        if (lotFilter?.Length > 100)
            return ApiResponse<InventoryItemHistoryReportDto>.ErrorResponse("Lot must not exceed 100 characters.", 400);
        var location = await Locations().Where(x => x.Id == locationId)
            .Select(x => new { x.Id, x.Code, x.Name }).FirstOrDefaultAsync(ct);
        if (location == null) return Denied<InventoryItemHistoryReportDto>();
        var item = await db.InventoryItems.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.Id == itemId && !x.IsDeleted && x.IsStockTracked)
            .Select(x => new { x.Id, x.Code, x.Name, x.UnitCode }).FirstOrDefaultAsync(ct);
        if (item == null)
            return ApiResponse<InventoryItemHistoryReportDto>.ErrorResponse("Stock item not found.", 404);

        // A legacy soft deletion of posted evidence must never make a stock history appear reconciled.
        var deletedEvidence = from line in db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            join movement in db.InventoryMovements.IgnoreQueryFilters().AsNoTracking()
                on new { line.TenantId, Id = line.InventoryMovementId }
                equals new { movement.TenantId, movement.Id }
            where line.TenantId == user.TenantId && line.InventoryItemId == itemId &&
                movement.StateCode == "Posted" && movement.PostedAt.HasValue &&
                (movement.FromLocationId == locationId || movement.ToLocationId == locationId) &&
                (line.IsDeleted || movement.IsDeleted) &&
                (!toUtc.HasValue || movement.PostedAt <= toUtc.Value)
            select new { line, movement };
        if (lotFilter != null)
            deletedEvidence = deletedEvidence.Where(x => (x.line.LotOrBatchNo ?? "") == lotFilter);
        if (await deletedEvidence.AnyAsync(ct))
            return ApiResponse<InventoryItemHistoryReportDto>.ErrorResponse(
                "Soft-deleted posted stock history requires reconciliation before reporting.", 409);

        var ledger = from line in db.InventoryMovementLines.AsNoTracking()
            join movement in db.InventoryMovements.AsNoTracking()
                on new { line.TenantId, Id = line.InventoryMovementId }
                equals new { movement.TenantId, movement.Id }
            where line.TenantId == user.TenantId && line.InventoryItemId == itemId &&
                !line.IsDeleted && !movement.IsDeleted &&
                movement.StateCode == "Posted" && movement.PostedAt.HasValue
            let incoming = ((movement.MovementTypeCode == "Receipt" || movement.MovementTypeCode == "Transfer") &&
                            movement.ToLocationId == locationId) ||
                           ((movement.MovementTypeCode == "ReversalIssue" || movement.MovementTypeCode == "ReversalTransfer") &&
                            movement.FromLocationId == locationId)
            let outgoing = ((movement.MovementTypeCode == "Issue" || movement.MovementTypeCode == "Transfer") &&
                            movement.FromLocationId == locationId) ||
                           ((movement.MovementTypeCode == "ReversalReceipt" || movement.MovementTypeCode == "ReversalTransfer") &&
                            movement.ToLocationId == locationId)
            where incoming || outgoing
            select new { line, movement, incoming };
        if (lotFilter != null)
            ledger = ledger.Where(x => (x.line.LotOrBatchNo ?? "") == lotFilter);

        decimal openingQty = 0m, openingCost = 0m;
        if (fromUtc.HasValue)
        {
            var opening = await ledger.Where(x => x.movement.PostedAt < fromUtc.Value)
                .GroupBy(x => 1)
                .Select(g => new {
                    Qty = g.Sum(x => x.incoming ? x.line.Quantity : -x.line.Quantity),
                    Cost = g.Sum(x => x.incoming ? x.line.Quantity * x.line.UnitCost :
                        -(x.line.Quantity * x.line.UnitCost))
                }).FirstOrDefaultAsync(ct);
            openingQty = opening?.Qty ?? 0m;
            openingCost = opening?.Cost ?? 0m;
        }

        var period = ledger;
        if (fromUtc.HasValue) period = period.Where(x => x.movement.PostedAt >= fromUtc.Value);
        if (toUtc.HasValue) period = period.Where(x => x.movement.PostedAt <= toUtc.Value);
        var totals = await period.GroupBy(x => 1).Select(g => new {
            InQty = g.Sum(x => x.incoming ? x.line.Quantity : 0m),
            OutQty = g.Sum(x => x.incoming ? 0m : x.line.Quantity),
            InCost = g.Sum(x => x.incoming ? x.line.Quantity * x.line.UnitCost : 0m),
            OutCost = g.Sum(x => x.incoming ? 0m : x.line.Quantity * x.line.UnitCost)
        }).FirstOrDefaultAsync(ct);
        var total = await period.CountAsync(ct);
        var rows = await period.OrderBy(x => x.movement.PostedAt)
            .ThenBy(x => x.movement.Id).ThenBy(x => x.line.LineNo).ThenBy(x => x.line.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new InventoryItemHistoryRowDto {
                MovementReference = x.movement.PublicId,
                MovementNumber = x.movement.MovementNumber,
                MovementTypeCode = x.movement.MovementTypeCode,
                PostedAtUtc = x.movement.PostedAt!.Value,
                LotOrBatchNo = x.line.LotOrBatchNo,
                QuantityIn = x.incoming ? x.line.Quantity : 0m,
                QuantityOut = x.incoming ? 0m : x.line.Quantity,
                RecordedCostIn = x.incoming ? x.line.Quantity * x.line.UnitCost : 0m,
                RecordedCostOut = x.incoming ? 0m : x.line.Quantity * x.line.UnitCost,
                SourceType = x.movement.SourceType,
                SourceId = x.movement.SourceId,
                IsReversal = x.movement.ReversalOfMovementId.HasValue
            }).ToListAsync(ct);

        var received = totals?.InQty ?? 0m;
        var issued = totals?.OutQty ?? 0m;
        var costIn = totals?.InCost ?? 0m;
        var costOut = totals?.OutCost ?? 0m;
        return ApiResponse<InventoryItemHistoryReportDto>.SuccessResponse(new InventoryItemHistoryReportDto {
            InventoryItemId = item.Id, ItemCode = item.Code, ItemName = item.Name, UnitCode = item.UnitCode,
            InventoryLocationId = location.Id, LocationName = location.Code + " — " + location.Name,
            FromUtc = fromUtc, ToUtc = toUtc, LotOrBatchNo = lotFilter,
            OpeningQuantity = openingQty, QuantityIn = received, QuantityOut = issued,
            ClosingQuantity = openingQty + received - issued,
            OpeningRecordedCost = openingCost, RecordedCostIn = costIn, RecordedCostOut = costOut,
            ClosingRecordedCost = openingCost + costIn - costOut,
            Page = page, PageSize = pageSize, TotalCount = total, Items = rows
        });
    }
}
