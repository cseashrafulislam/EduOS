using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Revalidates the immutable approved requisition ceiling and negotiated PO line provenance
/// at both submit and checker approval. Does not read or write stock/Finance ledgers.
/// </summary>
public sealed partial class ProcurementPurchaseOrderService
{
    private async Task<bool> CommitmentValidAsync(ProcurementPurchaseOrder order, CancellationToken ct)
    {
        if (!await db.ProcurementRequisitions.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == order.RequisitionId &&
                x.CampusId == order.CampusId && x.StateCode == "Approved" && !x.IsDeleted, ct) ||
            !await db.ProcurementSuppliers.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == order.SupplierId &&
                x.CampusId == order.CampusId && x.StateCode == "Approved" && !x.IsDeleted, ct))
            return false;

        var source = await db.ProcurementRequisitionLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.RequisitionId == order.RequisitionId && !x.IsDeleted)
            .Take(101).ToListAsync(ct);
        var committed = await db.ProcurementPurchaseOrderLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.PurchaseOrderId == order.Id && !x.IsDeleted)
            .Take(101).ToListAsync(ct);
        if (source.Count is < 1 or > 100 || committed.Count != source.Count ||
            source.Select(x => x.Id).Distinct().Count() != source.Count ||
            committed.Select(x => x.RequisitionLineId).Distinct().Count() != committed.Count ||
            source.Any(x => x.Quantity <= 0 || x.Quantity > 999999999999m ||
                x.Quantity != decimal.Round(x.Quantity, 6) ||
                x.EstimatedUnitPrice < 0 || x.EstimatedUnitPrice > 999999999999m ||
                x.EstimatedUnitPrice != decimal.Round(x.EstimatedUnitPrice, 4)))
            return false;

        var sourceById = source.ToDictionary(x => x.Id);
        foreach (var line in committed)
        {
            if (!sourceById.TryGetValue(line.RequisitionLineId, out var original) ||
                line.InventoryItemId != original.InventoryItemId || line.LineNo != original.LineNo ||
                line.Quantity <= 0 || line.Quantity > original.Quantity ||
                line.Quantity > 999999999m || line.Quantity != decimal.Round(line.Quantity, 6) ||
                line.UnitPrice < 0 || line.UnitPrice > 999999999m ||
                line.UnitPrice != decimal.Round(line.UnitPrice, 4))
                return false;
        }

        var approvedBudget = source.Sum(x => x.Quantity * x.EstimatedUnitPrice);
        var commitment = committed.Sum(x => x.Quantity * x.UnitPrice);
        if (commitment > approvedBudget) return false;

        var itemIds = source.Select(x => x.InventoryItemId).Distinct().ToArray();
        return await db.InventoryItems.AsNoTracking().CountAsync(x =>
            x.TenantId == user.TenantId && itemIds.Contains(x.Id) &&
            x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) == itemIds.Length;
    }
}
