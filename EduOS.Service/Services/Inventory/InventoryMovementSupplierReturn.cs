using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    /// <summary>Trusted, posted supplier return in the same SQL transaction as the
    /// independently reviewed payable credit-note journal.</summary>
    internal async Task<(InventoryMovement? Movement, string? Error)> PostSupplierReturnInTransactionAsync(
        ProcurementSupplierReturnCredit credit, ProcurementSupplierInvoiceLine billed,
        ProcurementGoodsReceipt receipt, CancellationToken ct)
    {
        if (!Postable || db.Database.CurrentTransaction == null || db.CurrentTenantId != user.TenantId ||
            credit.TenantId != user.TenantId || billed.TenantId != user.TenantId ||
            receipt.TenantId != user.TenantId || receipt.CampusId != credit.CampusId ||
            billed.SupplierInvoiceId != credit.SupplierInvoiceId ||
            billed.Id != credit.SupplierInvoiceLineId || credit.StateCode != "Submitted" ||
            !await LocationsAllowed(receipt.LocationId, null, ct))
            return (null, "Authorized supplier return, store and SQL transaction required.");
        var receiptLine = await db.ProcurementGoodsReceiptLines.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == billed.GoodsReceiptLineId &&
            x.GoodsReceiptId == receipt.Id && !x.IsDeleted, ct);
        if (receiptLine == null || receiptLine.InventoryItemId != billed.InventoryItemId ||
            receiptLine.LotOrBatchNo != billed.LotOrBatchNo ||
            receiptLine.PostedUnitCost != billed.StockUnitCostSnapshot ||
            credit.Quantity <= 0m || credit.Quantity > billed.Quantity ||
            decimal.Round(receiptLine.PostedUnitCost.Value * credit.Quantity, 2,
                MidpointRounding.AwayFromZero) != credit.StockValue)
            return (null, "Source receipt quantity, lot or stock-cost snapshot changed.");
        var now = clock.GetUtcNow().UtcDateTime;
        var guid = Guid.NewGuid();
        var movement = new InventoryMovement
        {
            TenantId = user.TenantId, PublicId = guid, ClientRequestId = credit.ClientRequestId,
            MovementNumber = "SPR-" + guid.ToString("N").ToUpperInvariant(),
            MovementTypeCode = "Issue", SourceType = "ProcurementSupplierReturn",
            SourceId = credit.Id, FromLocationId = receipt.LocationId,
            MovementAt = now, StateCode = "Draft", Remarks =
                "Approved supplier return " + credit.PublicId.ToString("N"),
            CreatedAt = now, CreatedBy = user.UserId
        };
        db.InventoryMovements.Add(movement); await db.SaveChangesAsync(ct);
        db.InventoryMovementLines.Add(new InventoryMovementLine
        {
            TenantId = user.TenantId, InventoryMovementId = movement.Id, LineNo = 1,
            InventoryItemId = billed.InventoryItemId, Quantity = credit.Quantity,
            LotOrBatchNo = billed.LotOrBatchNo, UnitCost = billed.StockUnitCostSnapshot,
            CreatedAt = now, CreatedBy = user.UserId
        });
        await db.SaveChangesAsync(ct);
        var (error, lines) = await CheckPostingAsync(movement, ct);
        if (error != null || lines.Count != 1 || lines[0].Quantity != credit.Quantity)
            return (null, error ?? "Return stock posting failed.");
        movement.StateCode = "Posted"; movement.PostedAt = now;
        movement.PostedByUserId = user.UserId; movement.UpdatedAt = now;
        movement.UpdatedBy = user.UserId;
        await db.SaveChangesAsync(ct);
        return (movement, null);
    }
}
