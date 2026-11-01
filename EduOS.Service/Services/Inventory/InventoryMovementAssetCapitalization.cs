using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    /// <summary>Trusted Inventory-ledger posting inside the asset/Finance transaction.
    /// Never exposed as a generic API or an unlinked ManualIssue.</summary>
    internal async Task<(InventoryMovement? Movement, InventoryMovementLine? Line, string? Error)>
        PostAssetIssueInTransactionAsync(AssetCapitalizationCommand command,
            ProcurementGoodsReceipt receipt, ProcurementGoodsReceiptLine receiptLine, CancellationToken ct)
    {
        if (!Postable || db.CurrentTenantId != user.TenantId || db.Database.CurrentTransaction == null ||
            command.TenantId != user.TenantId || receipt.TenantId != user.TenantId ||
            receiptLine.TenantId != user.TenantId || receiptLine.GoodsReceiptId != receipt.Id ||
            receipt.CampusId != command.CampusId || !await LocationsAllowed(receipt.LocationId, null, ct))
            return (null, null, "Trusted capitalization transaction, tenant and store required.");
        var now = clock.GetUtcNow().UtcDateTime;
        var reference = Guid.NewGuid();
        var movement = new InventoryMovement
        {
            TenantId = user.TenantId, PublicId = reference, ClientRequestId = command.ClientRequestId,
            MovementNumber = "ACI-" + reference.ToString("N").ToUpperInvariant(),
            MovementTypeCode = "Issue", SourceType = "AssetCapitalization",
            SourceId = command.AssetId, FromLocationId = receipt.LocationId,
            MovementAt = now, StateCode = "Draft",
            Remarks = "Approved fixed-asset capitalization " + command.PublicId.ToString("N"),
            CreatedAt = now, CreatedBy = user.UserId
        };
        db.InventoryMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        var stockLine = new InventoryMovementLine
        {
            TenantId = user.TenantId, InventoryMovementId = movement.Id, LineNo = 1,
            InventoryItemId = receiptLine.InventoryItemId, Quantity = 1m,
            LotOrBatchNo = receiptLine.LotOrBatchNo,
            UnitCost = receiptLine.PostedUnitCost ?? 0m,
            CreatedAt = now, CreatedBy = user.UserId
        };
        db.InventoryMovementLines.Add(stockLine);
        await db.SaveChangesAsync(ct);
        var (error, lines) = await CheckPostingAsync(movement, ct);
        if (error != null || lines.Count != 1 || lines[0].Id != stockLine.Id ||
            lines[0].UnitCost <= 0m)
            return (null, null, error ?? "Approved asset unit stock valuation is invalid.");
        movement.StateCode = "Posted";
        movement.PostedAt = now; movement.PostedByUserId = user.UserId;
        movement.UpdatedAt = now; movement.UpdatedBy = user.UserId;
        await db.SaveChangesAsync(ct);
        return (movement, stockLine, null);
    }
}
