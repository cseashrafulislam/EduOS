using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Procurement-owned negotiated PO draft amendments; never changes posted stock or GL.
/// Source requisition remains immutable and is the quantity/total budget ceiling.
/// </summary>
public sealed partial class ProcurementPurchaseOrderService
{
    public async Task<ApiResponse<ProcurementPurchaseOrderDto>> SaveDraftAsync(
        long id, SaveProcurementPurchaseOrderDraftRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Denied<ProcurementPurchaseOrderDto>();
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 10 or > 500 ||
            request.Lines == null || request.Lines.Count is < 1 or > 100 ||
            request.Lines.Any(x => x.RequisitionLineId <= 0 || x.Quantity <= 0 ||
                x.Quantity > 999999999m || x.Quantity != decimal.Round(x.Quantity, 6) ||
                x.UnitPrice < 0 || x.UnitPrice > 999999999m ||
                x.UnitPrice != decimal.Round(x.UnitPrice, 4)) ||
            request.Lines.Select(x => x.RequisitionLineId).Distinct().Count() != request.Lines.Count ||
            (request.ExpectedDeliveryDate.HasValue &&
                (request.ExpectedDeliveryDate.Value.TimeOfDay != TimeSpan.Zero ||
                 request.ExpectedDeliveryDate.Value.Date < clock.GetUtcNow().UtcDateTime.Date)))
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Invalid draft amendment.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var order = await db.ProcurementPurchaseOrders.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (order == null || !await CampusAllowed(order.CampusId, ct))
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Order not found.", 404);
            if (order.CreatedBy != user.UserId) return Denied<ProcurementPurchaseOrderDto>();
            if (order.StateCode != "Draft" || !Version(order.RowVersion, request.RowVersion))
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Draft changed or is locked.", 409);
            var requisition = await db.ProcurementRequisitions.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == order.RequisitionId &&
                x.CampusId == order.CampusId && !x.IsDeleted, ct);
            if (requisition?.StateCode != "Approved" ||
                !await db.ProcurementSuppliers.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == order.SupplierId &&
                    x.CampusId == order.CampusId && x.StateCode == "Approved" && !x.IsDeleted, ct))
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Source approval no longer valid.", 409);
            var source = await db.ProcurementRequisitionLines.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && x.RequisitionId == order.RequisitionId && !x.IsDeleted)
                .Take(101).ToListAsync(ct);
            var lines = await db.ProcurementPurchaseOrderLines.Where(x =>
                x.TenantId == user.TenantId && x.PurchaseOrderId == id && !x.IsDeleted)
                .Take(101).ToListAsync(ct);
            if (source.Count != request.Lines.Count || lines.Count != source.Count ||
                source.Count is < 1 or > 100 ||
                source.Select(x => x.Id).Distinct().Count() != source.Count)
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Source lines changed.", 409);
            var sourceById = source.ToDictionary(x => x.Id);
            var proposedById = request.Lines.ToDictionary(x => x.RequisitionLineId);
            if (lines.Any(x => !sourceById.TryGetValue(x.RequisitionLineId, out var original) ||
                original.InventoryItemId != x.InventoryItemId ||
                !proposedById.TryGetValue(x.RequisitionLineId, out var proposed) ||
                proposed.Quantity > original.Quantity))
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Quantity exceeds approved request.", 409);
            var approvedBudget = source.Sum(x => x.Quantity * x.EstimatedUnitPrice);
            var proposedTotal = request.Lines.Sum(x => x.Quantity * x.UnitPrice);
            if (proposedTotal > approvedBudget)
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Approved budget exceeded.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var line in lines)
            {
                var proposed = proposedById[line.RequisitionLineId];
                line.Quantity = proposed.Quantity; line.UnitPrice = proposed.UnitPrice;
                line.UpdatedAt = now; line.UpdatedBy = user.UserId;
            }
            order.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
            // Persist the reason in the existing audited field; submit clears the current reason.
            order.DecisionReason = request.Reason.Trim();
            order.UpdatedAt = now; order.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<ProcurementPurchaseOrderDto>.SuccessResponse(await MapAsync(order, ct));
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Competing amendment; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            logger.LogWarning(ex, "PO amendment integrity conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Amendment references changed.", 409);
        }
    }
}
