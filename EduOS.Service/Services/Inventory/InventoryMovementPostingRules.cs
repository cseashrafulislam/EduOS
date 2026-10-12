using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    private async Task<(string? Error, List<InventoryMovementLine> Lines)> CheckPostingAsync(InventoryMovement movement, CancellationToken ct)
    {
        var lines = await db.InventoryMovementLines.Where(x => x.TenantId == user.TenantId &&
            x.InventoryMovementId == movement.Id && !x.IsDeleted).OrderBy(x => x.LineNo).ToListAsync(ct);
        if (lines.Count is < 1 or > 100) return ("Invalid movement lines.", lines);
        var items = lines.Select(x => x.InventoryItemId).Distinct().ToArray();
        if (await db.InventoryItems.CountAsync(x => x.TenantId == user.TenantId && items.Contains(x.Id) &&
            x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != items.Length)
            return ("Inactive or missing inventory item.", lines);
        var locations = new[] { movement.FromLocationId, movement.ToLocationId }.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var balances = await Balances(items, locations, null, ct);
        var costs = new Dictionary<(long, string), decimal>();
        if (movement.MovementTypeCode != "Receipt")
        {
            foreach (var line in lines)
            {
                var key = (line.InventoryItemId, line.LotOrBatchNo ?? "");
                if (costs.ContainsKey(key)) continue;
                var available = Get(balances, key.Item1, movement.FromLocationId!.Value, key.Item2);
                if (available.Quantity <= 0m) return ("Insufficient item/lot stock.", lines);
                costs[key] = decimal.Round(available.Value / available.Quantity, 4, MidpointRounding.ToZero);
            }
        }
        foreach (var line in lines)
        {
            var lot = line.LotOrBatchNo ?? "";
            if (movement.MovementTypeCode != "Receipt") line.UnitCost = costs[(line.InventoryItemId, lot)];
            if (movement.FromLocationId.HasValue)
                Add(Get(balances, line.InventoryItemId, movement.FromLocationId.Value, lot), -line.Quantity, line.UnitCost);
            if (movement.ToLocationId.HasValue)
                Add(Get(balances, line.InventoryItemId, movement.ToLocationId.Value, lot), line.Quantity, line.UnitCost);
        }
        return Nonnegative(balances.Values) ? (null, lines) : ("Posting would create negative stock or invalid valuation.", lines);
    }
}