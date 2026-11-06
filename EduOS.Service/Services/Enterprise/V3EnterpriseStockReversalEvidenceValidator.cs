using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

// Read-only historical verification. The canonical stock movements remain the
// sole inventory ledger; this validator never repairs or reposts inventory.
internal static class V3EnterpriseStockReversalEvidenceValidator
{
    internal sealed record Reversal(long Id, long? OriginalId, long? SourceId,
        string? SourceType, string MovementTypeCode, string StateCode,
        long? FromLocationId, long? ToLocationId);

    private sealed record Line(long MovementId, int Number, long ItemId,
        decimal Quantity, decimal UnitCost, string? Lot, bool IsDeleted);

    internal static async Task<HashSet<long>> InvalidAsync(
        EduOSDbContext db, long tenantId, IReadOnlyList<Reversal> reversals, CancellationToken ct)
    {
        var invalid = new HashSet<long>();
        if (reversals.Count == 0) return invalid;
        if (reversals.Count > 50) return reversals.Select(x => x.Id).ToHashSet();

        var originalIds = reversals.Where(x => x.OriginalId is > 0)
            .Select(x => x.OriginalId!.Value).Distinct().ToArray();
        var originals = await db.InventoryMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && originalIds.Contains(x.Id))
            .Select(x => new { x.Id, x.IsDeleted, x.StateCode, x.MovementTypeCode,
                x.FromLocationId, x.ToLocationId })
            .ToDictionaryAsync(x => x.Id, ct);
        var duplicateIds = (await db.InventoryMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted && x.StateCode == "Posted" &&
                x.ReversalOfMovementId.HasValue && originalIds.Contains(x.ReversalOfMovementId.Value))
            .GroupBy(x => x.ReversalOfMovementId!.Value)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToArrayAsync(ct)).ToHashSet();

        foreach (var reversal in reversals)
            if (reversal.OriginalId is not > 0 || reversal.OriginalId == reversal.Id ||
                reversal.StateCode != "Posted" || reversal.SourceType != "MovementReversal" ||
                reversal.SourceId != reversal.OriginalId ||
                duplicateIds.Contains(reversal.OriginalId.Value) ||
                !originals.TryGetValue(reversal.OriginalId.Value, out var original) ||
                original.IsDeleted || original.StateCode != "Posted" ||
                original.MovementTypeCode is not ("Receipt" or "Issue" or "Transfer") ||
                reversal.MovementTypeCode != "Reversal" + original.MovementTypeCode ||
                reversal.FromLocationId != original.FromLocationId ||
                reversal.ToLocationId != original.ToLocationId)
                invalid.Add(reversal.Id);

        var eligible = reversals.Where(x => !invalid.Contains(x.Id)).ToArray();
        if (eligible.Length == 0) return invalid;
        var movementIds = eligible.SelectMany(x => new[] { x.Id, x.OriginalId!.Value })
            .Distinct().ToArray();
        var lines = await db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && movementIds.Contains(x.InventoryMovementId))
            .OrderBy(x => x.InventoryMovementId).ThenBy(x => x.LineNo).ThenBy(x => x.Id)
            .Select(x => new Line(x.InventoryMovementId, x.LineNo, x.InventoryItemId,
                x.Quantity, x.UnitCost, x.LotOrBatchNo, x.IsDeleted))
            .Take(10001).ToListAsync(ct);
        if (lines.Count > 10000)
            return invalid.Concat(eligible.Select(x => x.Id)).ToHashSet();
        var foreignIds = (await db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => movementIds.Contains(x.InventoryMovementId) && x.TenantId != tenantId)
            .Select(x => x.InventoryMovementId).Distinct().ToArrayAsync(ct)).ToHashSet();
        var byMovement = lines.GroupBy(x => x.MovementId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var reversal in eligible)
        {
            var originalId = reversal.OriginalId!.Value;
            if (foreignIds.Contains(reversal.Id) || foreignIds.Contains(originalId) ||
                !byMovement.TryGetValue(reversal.Id, out var actual) ||
                !byMovement.TryGetValue(originalId, out var expected) ||
                actual.Length is < 1 or > 100 || actual.Length != expected.Length ||
                actual.Any(x => x.IsDeleted) || expected.Any(x => x.IsDeleted))
            {
                invalid.Add(reversal.Id);
                continue;
            }
            for (var i = 0; i < actual.Length; i++)
                if (actual[i].Number != expected[i].Number || actual[i].ItemId != expected[i].ItemId ||
                    actual[i].Quantity != expected[i].Quantity || actual[i].UnitCost != expected[i].UnitCost ||
                    !string.Equals(actual[i].Lot ?? "", expected[i].Lot ?? "", StringComparison.Ordinal))
                {
                    invalid.Add(reversal.Id);
                    break;
                }
        }
        return invalid;
    }
}
