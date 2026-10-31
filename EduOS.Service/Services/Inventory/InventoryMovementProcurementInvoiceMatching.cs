using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Read-only preflight for an eventual supplier invoice. This is deliberately not a
/// payable or a duplicate-invoice clearance: durable invoice intake is a separate approval boundary.
/// </summary>
public sealed partial class InventoryMovementService
{
    private static ApiResponse<PreviewProcurementInvoiceDto> InvoiceMismatch(string message, int code = 409) =>
        ApiResponse<PreviewProcurementInvoiceDto>.ErrorResponse(message, code);

    public async Task<ApiResponse<PreviewProcurementInvoiceDto>> PreviewSupplierInvoiceMatchAsync(
        PreviewProcurementInvoiceRequest request, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 || db.CurrentTenantId != user.TenantId ||
            !(user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant")))
            return Denied<PreviewProcurementInvoiceDto>();
        if (request == null || request.PurchaseOrderId <= 0 ||
            request.PriceMode is not ("Automatic" or "ManualFinal" or "AutoPlusManual") ||
            request.Lines is not { Count: >= 1 and <= 100 } ||
            request.Lines.Any(x => x.GoodsReceiptReference == Guid.Empty || x.ReceiptLineNo <= 0 ||
                !ReceiptQty(x.Quantity) || !ReceiptPrice(x.Amount, true) ||
                x.VarianceReason?.Trim().Length > 800 ||
                (request.PriceMode == "Automatic" && (x.Amount != 0m || !string.IsNullOrWhiteSpace(x.VarianceReason)))) ||
            request.Lines.Select(x => (x.GoodsReceiptReference, x.ReceiptLineNo)).Distinct().Count() != request.Lines.Count)
            return InvoiceMismatch("Valid order, unique posted delivery lines, quantities and price policy required.", 400);

        // A PO belongs to exactly one campus and supplier. Campus visibility is checked
        // from authorized store/receipt rows, never from a caller-supplied campus ID.
        var po = await db.ProcurementPurchaseOrders.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == request.PurchaseOrderId && !x.IsDeleted, ct);
        if (po == null || !await Locations().AnyAsync(x => x.CampusId == po.CampusId, ct))
            return InvoiceMismatch("Purchase order not found.", 404);
        if (po.StateCode != "Approved" || po.CurrencyCode != "BDT")
            return InvoiceMismatch("Only an approved BDT order can be matched.");

        var references = request.Lines.Select(x => x.GoodsReceiptReference).Distinct().ToArray();
        var receipts = await VisibleReceipts().Where(x => references.Contains(x.PublicId))
            .ToDictionaryAsync(x => x.PublicId, ct);
        if (receipts.Count != references.Length || receipts.Values.Any(x =>
            x.PurchaseOrderId != po.Id || x.CampusId != po.CampusId ||
            x.StateCode != "Posted" || !x.PostedMovementId.HasValue))
            return InvoiceMismatch("Posted, authorized goods receipt evidence not found.", 404);

        if (!await db.ProcurementSuppliers.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.Id == po.SupplierId && x.CampusId == po.CampusId && !x.IsDeleted, ct))
            return InvoiceMismatch("Supplier or campus evidence missing.");

        var receiptIds = receipts.Values.Select(x => x.Id).ToArray();
        var sourceLines = await db.ProcurementGoodsReceiptLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && receiptIds.Contains(x.GoodsReceiptId) && !x.IsDeleted)
            .ToListAsync(ct);
        var poLines = await db.ProcurementPurchaseOrderLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.PurchaseOrderId == po.Id && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, ct);
        if (sourceLines.Any(x => !poLines.TryGetValue(x.PurchaseOrderLineId, out var original) ||
            original.InventoryItemId != x.InventoryItemId || original.Quantity != x.ApprovedPoQuantity ||
            original.UnitPrice != x.ApprovedPoUnitPrice))
            return InvoiceMismatch("Approved PO line differs from the captured delivery snapshot.");
        var byLine = sourceLines.ToDictionary(x => (x.GoodsReceiptId, x.LineNo));
        var movementIds = receipts.Values.Select(x => x.PostedMovementId!.Value).ToArray();
        var movements = await db.InventoryMovements.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && movementIds.Contains(x.Id) && !x.IsDeleted &&
            x.StateCode == "Posted" && x.SourceType == "ProcurementGoodsReceipt" &&
            x.MovementTypeCode == "Receipt" && x.ToLocationId != null)
            .ToDictionaryAsync(x => x.Id, ct);
        if (movements.Count != movementIds.Length || receipts.Values.Any(x =>
            !movements.TryGetValue(x.PostedMovementId!.Value, out var movement) ||
            movement.SourceId != x.Id || movement.ToLocationId != x.LocationId))
            return InvoiceMismatch("Posted inventory provenance is inconsistent.");
        if (await db.InventoryMovements.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.StateCode == "Posted" && x.ReversalOfMovementId.HasValue &&
            movementIds.Contains(x.ReversalOfMovementId.Value), ct))
            return InvoiceMismatch("Reversed inventory deliveries cannot support an invoice.");

        var postedLines = await db.InventoryMovementLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && movementIds.Contains(x.InventoryMovementId) && !x.IsDeleted)
            .ToListAsync(ct);
        // Compare the entire immutable delivery against its canonical stock movement,
        // including duplicate lots, not merely a caller-selected subset.
        foreach (var receipt in receipts.Values)
        {
            var deliveryLines = sourceLines.Where(x => x.GoodsReceiptId == receipt.Id).ToArray();
            var ledgerLines = postedLines.Where(x => x.InventoryMovementId == receipt.PostedMovementId).ToArray();
            if (deliveryLines.Length == 0 || deliveryLines.Length != ledgerLines.Length ||
                deliveryLines.GroupBy(x => (x.InventoryItemId, x.LotOrBatchNo, x.Quantity, Cost: x.PostedUnitCost))
                    .Any(g => g.Key.Cost == null || ledgerLines.Count(l =>
                        l.InventoryItemId == g.Key.InventoryItemId && (l.LotOrBatchNo ?? "") == g.Key.LotOrBatchNo &&
                        l.Quantity == g.Key.Quantity && l.UnitCost == g.Key.Cost) != g.Count()))
                return InvoiceMismatch("Delivery line values differ from the posted stock ledger.");
        }

        var result = new PreviewProcurementInvoiceDto
        {
            PurchaseOrderId = po.Id, OrderNumber = po.OrderNumber, SupplierId = po.SupplierId,
            CampusId = po.CampusId, PriceMode = request.PriceMode
        };
        try
        {
            foreach (var input in request.Lines)
            {
                var receipt = receipts[input.GoodsReceiptReference];
                if (!byLine.TryGetValue((receipt.Id, input.ReceiptLineNo), out var source) ||
                    input.Quantity > source.Quantity || source.PostedUnitCost == null)
                    return InvoiceMismatch("Invoice quantity exceeds the selected accepted delivery line.");
                decimal unitPrice = request.PriceMode switch
                {
                    "Automatic" => source.ApprovedPoUnitPrice,
                    "ManualFinal" => input.Amount,
                    _ => checked(source.ApprovedPoUnitPrice + input.Amount)
                };
                if (!ReceiptPrice(unitPrice) || unitPrice <= 0m)
                    return InvoiceMismatch("Invoice unit price must be positive and at most four decimals.", 400);
                var reason = input.VarianceReason?.Trim();
                if (unitPrice != source.ApprovedPoUnitPrice && (reason?.Length ?? 0) is < 10 or > 800)
                    return InvoiceMismatch("PO price override needs substantive reason (10–800 characters).", 400);
                var amount = decimal.Round(checked(input.Quantity * unitPrice), 2, MidpointRounding.AwayFromZero);
                var stockValue = decimal.Round(checked(input.Quantity * source.PostedUnitCost.Value), 2,
                    MidpointRounding.AwayFromZero);
                if (amount > 999999999999.99m || stockValue > 999999999999.99m)
                    return InvoiceMismatch("Invoice amount exceeds the bounded BDT preview range.", 400);
                var diff = amount - stockValue;
                result.Lines.Add(new PreviewProcurementInvoiceLineDto
                {
                    GoodsReceiptReference = input.GoodsReceiptReference, ReceiptLineNo = input.ReceiptLineNo,
                    PurchaseOrderLineId = source.PurchaseOrderLineId,
                    InventoryItemId = source.InventoryItemId, LotOrBatchNo = source.LotOrBatchNo,
                    Quantity = input.Quantity, ReceivedQuantity = source.Quantity,
                    ApprovedPoUnitPrice = source.ApprovedPoUnitPrice,
                    PostedStockUnitCost = source.PostedUnitCost.Value,
                    ProposedInvoiceUnitPrice = unitPrice, ProposedAmount = amount,
                    DifferenceFromStockValue = diff,
                    HasPriceVariance = diff != 0m || unitPrice != source.ApprovedPoUnitPrice,
                    VarianceReason = reason
                });
                result.ProposedAmount = checked(result.ProposedAmount + amount);
                result.DifferenceFromStockValue = checked(result.DifferenceFromStockValue + diff);
            }
        }
        catch (OverflowException)
        {
            return InvoiceMismatch("Proposed invoice amount is outside supported decimal range.", 400);
        }
        result.RequiresIndependentFinanceReview = result.Lines.Any(x => x.HasPriceVariance) || request.PriceMode != "Automatic";
        return ApiResponse<PreviewProcurementInvoiceDto>.SuccessResponse(result,
            "Read-only evidence preview; invoice uniqueness, cumulative billing and payable posting are not authorized by this result.");
    }
}
