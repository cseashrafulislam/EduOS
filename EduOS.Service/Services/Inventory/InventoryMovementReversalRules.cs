using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    private async Task<(string? Error, List<InventoryMovementLine> Lines)> CheckReversalAsync(InventoryMovement original, CancellationToken ct)
    {
        var lines = await db.InventoryMovementLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.InventoryMovementId == original.Id && !x.IsDeleted).OrderBy(x => x.LineNo).ToListAsync(ct);
        if (lines.Count == 0) return ("Original has no stock lines.", lines);
        var items = lines.Select(x => x.InventoryItemId).Distinct().ToArray();
        var locations = new[] { original.FromLocationId, original.ToLocationId }.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var balances = await Balances(items, locations, null, ct);
        foreach (var line in lines)
        {
            var lot = line.LotOrBatchNo ?? "";
            if (original.FromLocationId.HasValue)
                Add(Get(balances, line.InventoryItemId, original.FromLocationId.Value, lot), line.Quantity, line.UnitCost);
            if (original.ToLocationId.HasValue && original.SourceType != "TransferDispatch")
                Add(Get(balances, line.InventoryItemId, original.ToLocationId.Value, lot), -line.Quantity, line.UnitCost);
        }
        if (!Nonnegative(balances.Values))
            return ("Reversal would create negative stock or invalid valuation.", lines);
        var reserved = await ReservedQuantitiesAsync(items, locations, null, ct);
        foreach (var (key, quantity) in reserved)
            if (Get(balances, key.Item, key.Location, key.Lot).Quantity < quantity)
                return ("Reversal would consume reserved stock.", lines);
        return (null, lines);
    }
}