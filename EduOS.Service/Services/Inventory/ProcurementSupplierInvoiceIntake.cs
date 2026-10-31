using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class ProcurementSupplierInvoiceService
{
    private async Task<Dictionary<long, decimal>> CommittedAsync(long[] receiptLineIds, long? excludeId, CancellationToken ct)
    {
        var rows = await (from l in db.ProcurementSupplierInvoiceLines.AsNoTracking()
            join h in db.ProcurementSupplierInvoices.AsNoTracking() on l.SupplierInvoiceId equals h.Id
            where l.TenantId == user.TenantId && h.TenantId == user.TenantId &&
                receiptLineIds.Contains(l.GoodsReceiptLineId) && !l.IsDeleted && !h.IsDeleted &&
                (h.StateCode == "Submitted" || h.StateCode == "Posted") &&
                (!excludeId.HasValue || h.Id != excludeId.Value)
            group l by l.GoodsReceiptLineId into g
            select new { ReceiptLineId = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToListAsync(ct);
        return rows.ToDictionary(x => x.ReceiptLineId, x => x.Quantity);
    }
    private async Task<string?> RevalidateAsync(ProcurementSupplierInvoice invoice,
        IReadOnlyList<ProcurementSupplierInvoiceLine> lines, CancellationToken ct)
    {
        if (lines.Count is < 1 or > 100 || !await CampusAllowedAsync(invoice.CampusId, ct))
            return "Invoice source campus or lines missing.";
        var po = await db.ProcurementPurchaseOrders.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == invoice.PurchaseOrderId && !x.IsDeleted, ct);
        if (po == null || po.StateCode != "Approved" || po.SupplierId != invoice.SupplierId ||
            po.CampusId != invoice.CampusId || po.CurrencyCode != "BDT") return "Approved PO changed.";
        var receiptIds = lines.Select(x => x.GoodsReceiptLineId).Distinct().ToArray();
        if (receiptIds.Length != lines.Count) return "Repeated delivery line on supplier invoice.";
        var sources = await db.ProcurementGoodsReceiptLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            receiptIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        if (sources.Count != lines.Count) return "Delivery evidence missing.";
        var parentIds = sources.Values.Select(x => x.GoodsReceiptId).Distinct().ToArray();
        var receipts = await db.ProcurementGoodsReceipts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            parentIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        if (receipts.Count != parentIds.Length || receipts.Values.Any(x => x.PurchaseOrderId != po.Id ||
            x.CampusId != invoice.CampusId || x.StateCode != "Posted" || !x.PostedMovementId.HasValue))
            return "Only posted same-campus receipts may be billed.";
        var movementIds = receipts.Values.Select(x => x.PostedMovementId!.Value).ToArray();
        var movements = await db.InventoryMovements.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            movementIds.Contains(x.Id) && !x.IsDeleted && x.StateCode == "Posted" &&
            x.MovementTypeCode == "Receipt" && x.SourceType == "ProcurementGoodsReceipt")
            .ToDictionaryAsync(x => x.Id, ct);
        if (movements.Count != receipts.Count)
            return "Receipt stock movements missing.";
        if (receipts.Values.Any(x => !movements.TryGetValue(x.PostedMovementId!.Value, out var movement) ||
            movement.SourceId != x.Id || movement.ToLocationId != x.LocationId))
            return "Receipt-to-ledger provenance changed.";
        if (await db.InventoryMovements.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.StateCode == "Posted" && x.ReversalOfMovementId.HasValue &&
            movementIds.Contains(x.ReversalOfMovementId.Value), ct)) return "Stock receipt has been reversed.";
        if (lines.Any(x => !sources.TryGetValue(x.GoodsReceiptLineId, out var source) ||
            !Quantity(x.Quantity) || x.Quantity > source.Quantity ||
            x.ReceivedQuantitySnapshot != source.Quantity || x.PurchaseOrderLineId != source.PurchaseOrderLineId ||
            x.InventoryItemId != source.InventoryItemId || x.LotOrBatchNo != source.LotOrBatchNo ||
            x.PoUnitPriceSnapshot != source.ApprovedPoUnitPrice || x.StockUnitCostSnapshot != source.PostedUnitCost ||
            !Money(x.InvoiceAmount) || !Money(x.StockValue) || x.InvoiceUnitPrice <= 0m ||
            decimal.Round(x.Quantity * x.InvoiceUnitPrice, 2, MidpointRounding.AwayFromZero) != x.InvoiceAmount ||
            decimal.Round(x.Quantity * x.StockUnitCostSnapshot, 2, MidpointRounding.AwayFromZero) != x.StockValue))
            return "Immutable invoice/receipt/stock price or quantity no longer reconciles.";
        if (lines.Sum(x => x.InvoiceAmount) != invoice.TotalAmount ||
            lines.Sum(x => x.StockValue) != invoice.StockValue ||
            invoice.PriceVariance != invoice.TotalAmount - invoice.StockValue)
            return "Invoice totals no longer reconcile.";
        var preview = await inventory.PreviewSupplierInvoiceMatchAsync(new PreviewProcurementInvoiceRequest
        {
            PurchaseOrderId = invoice.PurchaseOrderId, PriceMode = invoice.PriceMode,
            Lines = lines.Select(x =>
            {
                var source = sources[x.GoodsReceiptLineId];
                var parent = receipts[source.GoodsReceiptId];
                return new PreviewProcurementInvoiceLineRequest
                {
                    GoodsReceiptReference = parent.PublicId, ReceiptLineNo = source.LineNo,
                    Quantity = x.Quantity,
                    Amount = invoice.PriceMode == "ManualFinal" ? x.InvoiceUnitPrice :
                        invoice.PriceMode == "AutoPlusManual" ? x.InvoiceUnitPrice - x.PoUnitPriceSnapshot : 0m,
                    VarianceReason = x.VarianceReason
                };
            }).ToList()
        }, ct);
        if (!preview.Success || preview.Data == null || preview.Data.ProposedAmount != invoice.TotalAmount ||
            preview.Data.DifferenceFromStockValue != invoice.PriceVariance ||
            preview.Data.Lines.Count != lines.Count)
            return "Three-way PO, delivery and canonical stock movement validation failed.";
        var claim = await CommittedAsync(receiptIds, invoice.Id, ct);
        if (lines.Any(x => claim.GetValueOrDefault(x.GoodsReceiptLineId) + x.Quantity >
            sources[x.GoodsReceiptLineId].Quantity)) return "Prior billed quantities exhaust delivery capacity.";
        return null;
    }

    public async Task<ApiResponse<ProcurementSupplierInvoiceDto>> CreateAsync(CreateProcurementSupplierInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (!CanMake) return Err<ProcurementSupplierInvoiceDto>("Supplier invoice maker permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.PurchaseOrderId <= 0 ||
            request.InvoiceDate == default || request.DueDate < request.InvoiceDate ||
            request.Lines is not { Count: >= 1 and <= 100 } ||
            string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber) ||
            request.SupplierInvoiceNumber.Trim().Length > 100 ||
            request.SupplierInvoiceNumber.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800)
            return Err<ProcurementSupplierInvoiceDto>("Invoice number, dates, quantity lines and reason (10–800) required.");
        var number = request.SupplierInvoiceNumber.Trim();
        var normalized = number.ToUpperInvariant();
        var reason = request.Reason.Trim();
        var hash = InvoiceCreateHash(request, normalized, reason);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.ProcurementSupplierInvoices.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, ct);
            if (existing != null)
            {
                if (existing.CreateFingerprint != hash) return Err<ProcurementSupplierInvoiceDto>("Invoice request key was reused for different data.", 409);
                if (!await CampusAllowedAsync(existing.CampusId, ct)) return Err<ProcurementSupplierInvoiceDto>("Invoice not found.", 404);
                return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(existing, ct), "Existing invoice returned.");
            }
            var preview = await inventory.PreviewSupplierInvoiceMatchAsync(new PreviewProcurementInvoiceRequest
            { PurchaseOrderId = request.PurchaseOrderId, PriceMode = request.PriceMode, Lines = request.Lines }, ct);
            if (!preview.Success || preview.Data == null)
                return Err<ProcurementSupplierInvoiceDto>(preview.Message, preview.StatusCode);
            var data = preview.Data;
            if (!await CampusAllowedAsync(data.CampusId, ct)) return Err<ProcurementSupplierInvoiceDto>("Invoice campus denied.", 403);
            if (await db.ProcurementSupplierInvoices.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.SupplierId == data.SupplierId && x.NormalizedSupplierInvoiceNumber == normalized, ct))
                return Err<ProcurementSupplierInvoiceDto>("Supplier invoice number already exists, including audit history.", 409);
            var refs = data.Lines.Select(x => x.GoodsReceiptReference).Distinct().ToArray();
            var receiptHeaders = await db.ProcurementGoodsReceipts.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && refs.Contains(x.PublicId) && !x.IsDeleted)
                .ToDictionaryAsync(x => x.PublicId, ct);
            var ids = receiptHeaders.Values.Select(x => x.Id).ToArray();
            var receiptLines = await db.ProcurementGoodsReceiptLines.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && ids.Contains(x.GoodsReceiptId) && !x.IsDeleted)
                .ToDictionaryAsync(x => (x.GoodsReceiptId, x.LineNo), ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var invoice = new ProcurementSupplierInvoice
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                PurchaseOrderId = request.PurchaseOrderId, SupplierId = data.SupplierId, CampusId = data.CampusId,
                SupplierInvoiceNumber = number, NormalizedSupplierInvoiceNumber = normalized,
                InvoiceDate = request.InvoiceDate, DueDate = request.DueDate,
                PriceMode = request.PriceMode, Reason = reason, CreateFingerprint = hash,
                TotalAmount = data.ProposedAmount, StockValue = data.ProposedAmount - data.DifferenceFromStockValue,
                PriceVariance = data.DifferenceFromStockValue, CreatedAt = now, CreatedBy = user.UserId
            };
            db.ProcurementSupplierInvoices.Add(invoice);
            await db.SaveChangesAsync(ct);
            var lines = data.Lines.Select((x, index) =>
            {
                var receipt = receiptHeaders[x.GoodsReceiptReference];
                var origin = receiptLines[(receipt.Id, x.ReceiptLineNo)];
                return new ProcurementSupplierInvoiceLine
                {
                    TenantId = user.TenantId, SupplierInvoiceId = invoice.Id, LineNo = index + 1,
                    GoodsReceiptLineId = origin.Id, PurchaseOrderLineId = origin.PurchaseOrderLineId,
                    InventoryItemId = x.InventoryItemId, LotOrBatchNo = x.LotOrBatchNo,
                    Quantity = x.Quantity, ReceivedQuantitySnapshot = x.ReceivedQuantity,
                    PoUnitPriceSnapshot = x.ApprovedPoUnitPrice, StockUnitCostSnapshot = x.PostedStockUnitCost,
                    InvoiceUnitPrice = x.ProposedInvoiceUnitPrice, InvoiceAmount = x.ProposedAmount,
                    StockValue = x.ProposedAmount - x.DifferenceFromStockValue,
                    VarianceReason = x.VarianceReason, CreatedAt = now, CreatedBy = user.UserId
                };
            }).ToList();
            db.ProcurementSupplierInvoiceLines.AddRange(lines);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(invoice, ct),
                "Supplier invoice draft captured; no payable posted.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Supplier invoice create conflict"); return Err<ProcurementSupplierInvoiceDto>("Invoice number or request key conflict.",409); }
        catch (SqlException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Supplier invoice create SQL race"); return Err<ProcurementSupplierInvoiceDto>("Concurrent invoice create conflict.",409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Invoice create wrapped SQL race"); return Err<ProcurementSupplierInvoiceDto>("Concurrent invoice conflict.",409); }
    }

    public async Task<ApiResponse<ProcurementSupplierInvoiceDto>> SubmitAsync(Guid reference,
        SubmitProcurementSupplierInvoiceRequest request, CancellationToken ct = default)
    {
        if (!CanMake) return Err<ProcurementSupplierInvoiceDto>("Supplier invoice maker permission required.",403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty)
            return Err<ProcurementSupplierInvoiceDto>("Invoice reference and unique submission key required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var invoice = await db.ProcurementSupplierInvoices.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted, ct);
            if (invoice == null || !await CampusAllowedAsync(invoice.CampusId, ct))
                return Err<ProcurementSupplierInvoiceDto>("Supplier invoice not found.",404);
            if (invoice.CreatedBy != user.UserId) return Err<ProcurementSupplierInvoiceDto>("Only the invoice maker may submit.",403);
            if (invoice.SubmitRequestId == request.ClientRequestId)
                return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(invoice,ct),"Already submitted.");
            if (invoice.StateCode != "Draft" || !Version(invoice.RowVersion, request.RowVersion))
                return Err<ProcurementSupplierInvoiceDto>("Invoice state or version changed.",409);
            var lines = await db.ProcurementSupplierInvoiceLines.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.SupplierInvoiceId == invoice.Id && !x.IsDeleted).ToListAsync(ct);
            var error = await RevalidateAsync(invoice, lines, ct);
            if (error != null) return Err<ProcurementSupplierInvoiceDto>(error,409);
            var now = clock.GetUtcNow().UtcDateTime;
            invoice.StateCode = "Submitted"; invoice.SubmitRequestId = request.ClientRequestId;
            invoice.SubmittedAtUtc = now; invoice.SubmittedByUserId = user.UserId;
            invoice.UpdatedAt = now; invoice.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(invoice,ct),"Submitted to independent Finance reviewer.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex,"Invoice submit conflict"); return Err<ProcurementSupplierInvoiceDto>("Invoice submit conflict.",409); }
        catch (SqlException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Invoice submit SQL race"); return Err<ProcurementSupplierInvoiceDto>("Concurrent invoice submission conflict.",409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Invoice submit wrapped SQL race"); return Err<ProcurementSupplierInvoiceDto>("Invoice submission conflict.",409); }
    }
}
