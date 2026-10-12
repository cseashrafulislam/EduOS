using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    private async Task<(InventoryMovement Movement, List<InventoryMovementLine> Lines)> PersistDraftAsync(CreateInventoryMovementRequestDto request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var movement = NewDraft(request, now);
        db.InventoryMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        var lines = request.Lines.Select((x, i) => new InventoryMovementLine {
            TenantId = user.TenantId, InventoryMovementId = movement.Id, LineNo = i + 1,
            InventoryItemId = x.InventoryItemId, Quantity = x.Quantity, UnitCost = x.UnitCost,
            LotOrBatchNo = x.LotOrBatchNo?.Trim() ?? "", CreatedAt = now, CreatedBy = user.UserId
        }).ToList();
        db.InventoryMovementLines.AddRange(lines);
        await db.SaveChangesAsync(ct);
        return (movement, lines);
    }
}