using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

/// <summary>
/// Bounded, read-only verification of canonical dispatch-to-receipt evidence.
/// No inferred procurement receipt, asset capitalization or Finance posting.
/// Shared by detail and paged register to prevent contradictory statuses.
/// </summary>
internal static class V3EnterpriseTransferEvidenceValidator
{
    internal sealed record Receipt(
        long Id, long? SourceId, long? ToLocationId, string StateCode, string MovementTypeCode);

    private sealed record Line(
        long MovementId, int LineNo, long ItemId, decimal Quantity,
        decimal UnitCost, string? LotOrBatchNo, bool IsDeleted);

    internal static async Task<HashSet<long>> InvalidReceiptsAsync(
        EduOSDbContext db, long tenantId, IReadOnlyList<Receipt> receipts,
        CancellationToken ct)
    {
        var invalid = new HashSet<long>();
        if (receipts.Count == 0) return invalid;
        if (receipts.Count > 50) return receipts.Select(x => x.Id).ToHashSet();

        var dispatchIds = receipts.Where(x => x.SourceId is > 0)
            .Select(x => x.SourceId!.Value).Distinct().ToArray();
        var dispatches = await db.InventoryMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && dispatchIds.Contains(x.Id))
            .Select(x => new {
                x.Id, x.IsDeleted, x.SourceType, x.MovementTypeCode,
                x.StateCode, x.ToLocationId
            }).ToDictionaryAsync(x => x.Id, ct);

        foreach (var receipt in receipts)
            if (receipt.SourceId is not > 0 || !receipt.ToLocationId.HasValue ||
                receipt.StateCode != "Posted" || receipt.MovementTypeCode != "Receipt" ||
                receipt.SourceId == receipt.Id ||
                !dispatches.TryGetValue(receipt.SourceId.Value, out var dispatch) ||
                dispatch.IsDeleted || dispatch.SourceType != "TransferDispatch" ||
                dispatch.MovementTypeCode != "Issue" || dispatch.StateCode != "Posted" ||
                dispatch.ToLocationId != receipt.ToLocationId)
                invalid.Add(receipt.Id);

        var eligible = receipts.Where(x => !invalid.Contains(x.Id)).ToArray();
        if (eligible.Length == 0) return invalid;
        var movementIds = eligible.SelectMany(x => new[] { x.Id, x.SourceId!.Value })
            .Distinct().ToArray();

        // Fifty receipts and fifty dispatches, at most 100 lines each.
        // A corrupt oversized set fails closed instead of being truncated.
        var lines = await db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && movementIds.Contains(x.InventoryMovementId))
            .OrderBy(x => x.InventoryMovementId).ThenBy(x => x.LineNo).ThenBy(x => x.Id)
            .Select(x => new Line(x.InventoryMovementId, x.LineNo, x.InventoryItemId,
                x.Quantity, x.UnitCost, x.LotOrBatchNo, x.IsDeleted))
            .Take(10001).ToListAsync(ct);
        if (lines.Count > 10000)
        {
            foreach (var receipt in eligible) invalid.Add(receipt.Id);
            return invalid;
        }

        var groups = lines.GroupBy(x => x.MovementId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var receipt in eligible)
        {
            if (!groups.TryGetValue(receipt.Id, out var actual) ||
                !groups.TryGetValue(receipt.SourceId!.Value, out var expected) ||
                actual.Length is < 1 or > 100 || expected.Length != actual.Length ||
                actual.Any(x => x.IsDeleted) || expected.Any(x => x.IsDeleted))
            {
                invalid.Add(receipt.Id);
                continue;
            }
            for (var i = 0; i < actual.Length; i++)
            {
                var a = actual[i];
                var b = expected[i];
                if (a.LineNo != b.LineNo || a.ItemId != b.ItemId ||
                    a.Quantity != b.Quantity || a.UnitCost != b.UnitCost ||
                    !string.Equals(a.LotOrBatchNo, b.LotOrBatchNo, StringComparison.Ordinal))
                {
                    invalid.Add(receipt.Id);
                    break;
                }
            }
        }
        return invalid;
    }
}
