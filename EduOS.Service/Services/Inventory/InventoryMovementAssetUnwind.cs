using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    /// <summary>Only the independent Asset Finance reviewer can invoke this inside
    /// the very same transaction that posts the exact capitalization GL contra.</summary>
    internal async Task<(InventoryMovement? Reversal, string? Error)> ReverseAssetIssueInTransactionAsync(
        AssetCapitalizationCommand command, Guid clientKey, string reason, CancellationToken ct)
    {
        if (!Postable || db.CurrentTenantId != user.TenantId || db.Database.CurrentTransaction == null ||
            command.TenantId != user.TenantId || command.StockMovementId is null or <= 0 ||
            command.StateCode != "Posted" || clientKey == Guid.Empty)
            return (null, "Trusted, approved asset correction transaction required.");
        var original = await db.InventoryMovements.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
            x.Id == command.StockMovementId && !x.IsDeleted, ct);
        if (original == null || original.StateCode != "Posted" || original.MovementTypeCode != "Issue" ||
            original.SourceType != "AssetCapitalization" || original.SourceId != command.AssetId ||
            original.ReversalOfMovementId.HasValue || original.FromLocationId is null or <= 0 ||
            !await LocationsAllowed(original.FromLocationId, null, ct) ||
            await db.InventoryMovements.AnyAsync(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.ReversalOfMovementId == original.Id, ct))
            return (null, "Capitalization stock movement changed or is already corrected.");
        var (error, lines) = await CheckReversalAsync(original, ct);
        if (error != null || lines.Count != 1 || lines[0].Quantity != 1m || lines[0].UnitCost <= 0m)
            return (null, error ?? "Canonical serialized stock unit is inconsistent.");
        var (reversal, _) = await PersistReversalAsync(original, lines, clientKey, reason, ct);
        return (reversal, null);
    }
}
