using EduOS.Core.Entities.Inventory;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    private async Task<(InventoryMovement Movement, List<InventoryMovementLine> Lines)> PersistReversalAsync(
        InventoryMovement original, IReadOnlyList<InventoryMovementLine> lines, Guid requestId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var reference = Guid.NewGuid();
        var reversal = new InventoryMovement {
            TenantId = user.TenantId, PublicId = reference, ClientRequestId = requestId,
            MovementNumber = "IM-" + reference.ToString("N").ToUpperInvariant(),
            MovementTypeCode = "Reversal" + original.MovementTypeCode, MovementAt = now,
            FromLocationId = original.FromLocationId, ToLocationId = original.ToLocationId,
            SourceType = "MovementReversal", SourceId = original.Id, ReversalOfMovementId = original.Id,
            Remarks = reason, StateCode = "Posted", PostedAt = now, PostedByUserId = user.UserId,
            CreatedAt = now, CreatedBy = user.UserId
        };
        db.InventoryMovements.Add(reversal);
        await db.SaveChangesAsync(ct);
        var copies = lines.Select(x => new InventoryMovementLine {
            TenantId = user.TenantId, InventoryMovementId = reversal.Id, LineNo = x.LineNo,
            InventoryItemId = x.InventoryItemId, Quantity = x.Quantity, UnitCost = x.UnitCost,
            LotOrBatchNo = x.LotOrBatchNo, CreatedAt = now, CreatedBy = user.UserId
        }).ToList();
        db.InventoryMovementLines.AddRange(copies);
        await db.SaveChangesAsync(ct);
        return (reversal, copies);
    }
}