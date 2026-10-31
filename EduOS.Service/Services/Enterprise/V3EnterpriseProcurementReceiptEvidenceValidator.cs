using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

/// <summary>
/// Read-only, bounded verification of the canonical PO -> accepted delivery ->
/// posted InventoryMovement chain. This does not assert a Finance journal exists.
/// </summary>
public static class V3EnterpriseProcurementReceiptEvidenceValidator
{
    public sealed record Movement(long Id, long? SourceId, long? FromLocationId,
        long? ToLocationId, string StateCode, string MovementTypeCode);
    public sealed record Source(Guid GoodsReceiptReference, Guid PurchaseOrderReference);
    public sealed class Result
    {
        public HashSet<long> Invalid { get; } = [];
        public Dictionary<long, Source> Verified { get; } = [];
    }

    private readonly record struct LineKey(long ItemId, string Lot, decimal Quantity, decimal Cost);

    public static async Task<Result> InspectAsync(EduOSDbContext db, long tenantId,
        IReadOnlyList<Movement> movements, CancellationToken ct = default)
    {
        var result = new Result();
        if (movements.Count == 0) return result;
        if (movements.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(movements), "Maximum 50 movement references per inspection.");
        var ids = movements.Select(x => x.Id).ToArray();
        var receiptIds = movements.Where(x => x.SourceId is > 0)
            .Select(x => x.SourceId!.Value).Distinct().ToArray();
        var receipts = await db.ProcurementGoodsReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && receiptIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.PurchaseOrderId, x.CampusId,
                x.LocationId, x.StateCode, x.PostedMovementId, x.CreatedBy,
                x.SubmittedByUserId, x.ReviewedByUserId, x.SubmittedAtUtc,
                x.ReviewedAtUtc, x.ReviewReason, x.ValuationMode, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var poIds = receipts.Values.Select(x => x.PurchaseOrderId).Distinct().ToArray();
        var locationIds = receipts.Values.Select(x => x.LocationId).Distinct().ToArray();
        var orders = await db.ProcurementPurchaseOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && poIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.CampusId, x.StateCode, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var locations = await db.InventoryLocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && locationIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CampusId, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var poLines = await db.ProcurementPurchaseOrderLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && poIds.Contains(x.PurchaseOrderId))
            .Select(x => new { x.Id, x.PurchaseOrderId, x.InventoryItemId,
                x.Quantity, x.UnitPrice, x.IsDeleted }).Take(5001).ToListAsync(ct);
        var receiptLines = await db.ProcurementGoodsReceiptLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && receiptIds.Contains(x.GoodsReceiptId))
            .Select(x => new { x.GoodsReceiptId, x.LineNo, x.PurchaseOrderLineId,
                x.InventoryItemId, x.LotOrBatchNo, x.Quantity, x.PostedUnitCost,
                x.ApprovedPoQuantity, x.ApprovedPoUnitPrice, x.IsDeleted })
            .Take(5001).ToListAsync(ct);
        var stockLines = await db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.InventoryMovementId))
            .Select(x => new { x.InventoryMovementId, x.InventoryItemId, x.LotOrBatchNo,
                x.Quantity, x.UnitCost, x.IsDeleted }).Take(5001).ToListAsync(ct);
        var links = await db.InventoryMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SourceType == "ProcurementGoodsReceipt" &&
                x.SourceId.HasValue && receiptIds.Contains(x.SourceId.Value))
            .Select(x => new { x.Id, x.SourceId, x.IsDeleted })
            .Take(5001).ToListAsync(ct);
        if (poLines.Count > 5000 || receiptLines.Count > 5000 ||
            stockLines.Count > 5000 || links.Count > 5000)
        {
            foreach (var movement in movements) result.Invalid.Add(movement.Id);
            return result;
        }

        var poLineMap = poLines.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var receiptGroups = receiptLines.GroupBy(x => x.GoodsReceiptId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var stockGroups = stockLines.GroupBy(x => x.InventoryMovementId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var linkGroups = links.GroupBy(x => x.SourceId!.Value)
            .ToDictionary(g => g.Key, g => g.ToArray());

        foreach (var movement in movements)
        {
            if (movement.SourceId is not > 0 ||
                !receipts.TryGetValue(movement.SourceId.Value, out var receipt) ||
                !orders.TryGetValue(receipt.PurchaseOrderId, out var po) ||
                !locations.TryGetValue(receipt.LocationId, out var location) ||
                receipt.IsDeleted || po.IsDeleted || location.IsDeleted ||
                po.CampusId != receipt.CampusId || location.CampusId != receipt.CampusId ||
                po.StateCode is not ("Approved" or "CancelRequested") ||
                receipt.StateCode != "Posted" || receipt.PostedMovementId != movement.Id ||
                movement.StateCode != "Posted" || movement.MovementTypeCode != "Receipt" ||
                movement.FromLocationId.HasValue || movement.ToLocationId != receipt.LocationId ||
                receipt.CreatedBy is null or <= 0 ||
                receipt.SubmittedByUserId is null or <= 0 ||
                receipt.ReviewedByUserId is null or <= 0 ||
                receipt.CreatedBy == receipt.ReviewedByUserId ||
                receipt.SubmittedByUserId == receipt.ReviewedByUserId ||
                !receipt.SubmittedAtUtc.HasValue || !receipt.ReviewedAtUtc.HasValue ||
                receipt.ReviewedAtUtc < receipt.SubmittedAtUtc ||
                string.IsNullOrWhiteSpace(receipt.ReviewReason) ||
                receipt.ValuationMode is not ("Automatic" or "ManualFinal" or "AutoPlusManual") ||
                !linkGroups.TryGetValue(receipt.Id, out var sourceLinks) ||
                sourceLinks.Length != 1 || sourceLinks[0].Id != movement.Id ||
                sourceLinks[0].IsDeleted ||
                !receiptGroups.TryGetValue(receipt.Id, out var delivered) ||
                !stockGroups.TryGetValue(movement.Id, out var posted) ||
                delivered.Length is < 1 or > 100 || delivered.Length != posted.Length ||
                delivered.Select(x => x.LineNo).Distinct().Count() != delivered.Length ||
                delivered.Any(x => x.IsDeleted || x.Quantity <= 0m ||
                    !x.PostedUnitCost.HasValue || x.PostedUnitCost.Value < 0m ||
                    !poLineMap.TryGetValue(x.PurchaseOrderLineId, out var poLine) ||
                    poLine.IsDeleted || poLine.PurchaseOrderId != receipt.PurchaseOrderId ||
                    poLine.InventoryItemId != x.InventoryItemId ||
                    poLine.Quantity != x.ApprovedPoQuantity ||
                    poLine.UnitPrice != x.ApprovedPoUnitPrice) ||
                posted.Any(x => x.IsDeleted || x.Quantity <= 0m || x.UnitCost < 0m))
            {
                result.Invalid.Add(movement.Id);
                continue;
            }

            var expected = delivered.GroupBy(x => new LineKey(x.InventoryItemId,
                x.LotOrBatchNo ?? "", x.Quantity, x.PostedUnitCost!.Value))
                .ToDictionary(g => g.Key, g => g.Count());
            var actual = posted.GroupBy(x => new LineKey(x.InventoryItemId,
                x.LotOrBatchNo ?? "", x.Quantity, x.UnitCost))
                .ToDictionary(g => g.Key, g => g.Count());
            if (expected.Count != actual.Count ||
                expected.Any(x => !actual.TryGetValue(x.Key, out var count) || count != x.Value))
            {
                result.Invalid.Add(movement.Id);
                continue;
            }
            result.Verified[movement.Id] = new Source(receipt.PublicId, po.PublicId);
        }
        return result;
    }
}
