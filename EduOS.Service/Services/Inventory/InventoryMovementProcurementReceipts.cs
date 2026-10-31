using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>The Inventory ledger owner alone performs the trusted procurement stock posting.</summary>
public sealed partial class InventoryMovementService
{
    private const decimal ReceiptLimit = 999999999999m;

    private IQueryable<ProcurementGoodsReceipt> VisibleReceipts() =>
        db.ProcurementGoodsReceipts.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
            Locations().Select(l => l.Id).Contains(x.LocationId));

    private static bool ReceiptQty(decimal value) =>
        value > 0m && value <= ReceiptLimit && Precision(value, 6);
    private static bool ReceiptPrice(decimal value, bool signed = false) =>
        value >= (signed ? -ReceiptLimit : 0m) && value <= ReceiptLimit && Precision(value, 4);
    private static ApiResponse<ProcurementGoodsReceiptDto> ReceiptConflict(string message) =>
        ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse(message, 409);

    private static string ReceiptCreateHash(CreateProcurementGoodsReceiptRequest request, string reason) => Fingerprint(new
    {
        request.PurchaseOrderId, request.LocationId, Reason = reason,
        Lines = request.Lines.OrderBy(x => x.PurchaseOrderLineId)
            .ThenBy(x => x.LotOrBatchNo?.Trim() ?? "")
            .Select(x => new { x.PurchaseOrderLineId, x.Quantity, Lot = x.LotOrBatchNo?.Trim() ?? "" }).ToArray()
    });

    private static string ReceiptReviewHash(ReviewProcurementGoodsReceiptRequest request, string reason) => Fingerprint(new
    {
        request.Decision, request.ValuationMode, Reason = reason,
        Lines = request.Valuations.OrderBy(x => x.LineNo)
            .Select(x => new { x.LineNo, x.Amount, Reason = x.Reason?.Trim() ?? "" }).ToArray()
    });

    private async Task<ProcurementGoodsReceiptDto> ReceiptDtoAsync(ProcurementGoodsReceipt receipt, CancellationToken ct)
    {
        var rows = await db.ProcurementGoodsReceiptLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.GoodsReceiptId == receipt.Id && !x.IsDeleted)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        var orderNumber = await db.ProcurementPurchaseOrders.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.Id == receipt.PurchaseOrderId && !x.IsDeleted)
            .Select(x => x.OrderNumber).FirstOrDefaultAsync(ct) ?? "";
        Guid? movementRef = receipt.PostedMovementId.HasValue
            ? await db.InventoryMovements.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.Id == receipt.PostedMovementId && !x.IsDeleted).Select(x => (Guid?)x.PublicId)
                .FirstOrDefaultAsync(ct) : null;
        return new ProcurementGoodsReceiptDto
        {
            Reference = receipt.PublicId, PurchaseOrderId = receipt.PurchaseOrderId, OrderNumber = orderNumber,
            CampusId = receipt.CampusId, LocationId = receipt.LocationId,
            StateCode = receipt.StateCode, Reason = receipt.Reason,
            CreatedAt = receipt.CreatedAt, CreatedByUserId = receipt.CreatedBy,
            SubmittedAtUtc = receipt.SubmittedAtUtc, ReviewedAtUtc = receipt.ReviewedAtUtc,
            ReviewedByUserId = receipt.ReviewedByUserId, ReviewReason = receipt.ReviewReason,
            ValuationMode = receipt.ValuationMode, PostedMovementReference = movementRef,
            RowVersion = Convert.ToBase64String(receipt.RowVersion),
            Lines = rows.Select(x => new ProcurementGoodsReceiptLineDto
            {
                LineNo = x.LineNo, PurchaseOrderLineId = x.PurchaseOrderLineId,
                InventoryItemId = x.InventoryItemId, LotOrBatchNo = x.LotOrBatchNo,
                Quantity = x.Quantity, ApprovedPoQuantity = x.ApprovedPoQuantity,
                ApprovedPoUnitPrice = x.ApprovedPoUnitPrice, PostedUnitCost = x.PostedUnitCost,
                ValuationReason = x.ValuationReason
            }).ToList()
        };
    }

    public async Task<ApiResponse<PagedResult<ProcurementGoodsReceiptDto>>> GetGoodsReceiptsAsync(
        long? purchaseOrderId, string? state, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId) return Denied<PagedResult<ProcurementGoodsReceiptDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            purchaseOrderId is <= 0 || (!string.IsNullOrWhiteSpace(state) &&
            state is not ("Draft" or "Submitted" or "Posted" or "Rejected")))
            return ApiResponse<PagedResult<ProcurementGoodsReceiptDto>>.ErrorResponse("Invalid receipt filters.");
        var query = VisibleReceipts();
        if (purchaseOrderId.HasValue) query = query.Where(x => x.PurchaseOrderId == purchaseOrderId.Value);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state);
        var total = await query.CountAsync(ct);
        var headers = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = new List<ProcurementGoodsReceiptDto>();
        foreach (var row in headers) items.Add(await ReceiptDtoAsync(row, ct));
        return ApiResponse<PagedResult<ProcurementGoodsReceiptDto>>.SuccessResponse(new PagedResult<ProcurementGoodsReceiptDto>
        { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<ProcurementGoodsReceiptDto>> GetGoodsReceiptAsync(Guid reference, CancellationToken ct = default)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId) return Denied<ProcurementGoodsReceiptDto>();
        var receipt = await VisibleReceipts().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        return receipt == null
            ? ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Goods receipt not found.", 404)
            : ApiResponse<ProcurementGoodsReceiptDto>.SuccessResponse(await ReceiptDtoAsync(receipt, ct));
    }

    private async Task<Dictionary<long, decimal>> ReceiptCommittedQuantitiesAsync(
        long purchaseOrderId, long? excludeReceiptId, CancellationToken ct)
    {
        var rows = await (from header in db.ProcurementGoodsReceipts.AsNoTracking()
            join line in db.ProcurementGoodsReceiptLines.AsNoTracking() on header.Id equals line.GoodsReceiptId
            where header.TenantId == user.TenantId && line.TenantId == user.TenantId &&
                header.PurchaseOrderId == purchaseOrderId && !header.IsDeleted && !line.IsDeleted &&
                (header.StateCode == "Submitted" || header.StateCode == "Posted") &&
                (!excludeReceiptId.HasValue || header.Id != excludeReceiptId.Value)
            group line by line.PurchaseOrderLineId into g
            select new { PoLineId = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToListAsync(ct);
        return rows.ToDictionary(x => x.PoLineId, x => x.Quantity);
    }

    private async Task<bool> ReceiptSourceValidAsync(ProcurementGoodsReceipt receipt,
        IReadOnlyList<ProcurementGoodsReceiptLine> lines, CancellationToken ct)
    {
        if (!await LocationsAllowed(null, receipt.LocationId, ct) ||
            !await db.InventoryLocations.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.Id == receipt.LocationId && x.CampusId == receipt.CampusId && x.IsActive && !x.IsDeleted, ct))
            return false;
        var order = await db.ProcurementPurchaseOrders.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == receipt.PurchaseOrderId && !x.IsDeleted, ct);
        if (order == null || order.StateCode != "Approved" || order.CampusId != receipt.CampusId ||
            !await db.ProcurementSuppliers.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.Id == order.SupplierId && x.CampusId == receipt.CampusId &&
                x.StateCode == "Approved" && !x.IsDeleted, ct)) return false;
        var poLines = await db.ProcurementPurchaseOrderLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.PurchaseOrderId == order.Id && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, ct);
        var itemIds = lines.Select(x => x.InventoryItemId).Distinct().ToArray();
        if (await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
            itemIds.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != itemIds.Length)
            return false;
        return lines.All(x => poLines.TryGetValue(x.PurchaseOrderLineId, out var poLine) &&
            poLine.InventoryItemId == x.InventoryItemId && poLine.Quantity == x.ApprovedPoQuantity &&
            poLine.UnitPrice == x.ApprovedPoUnitPrice && ReceiptQty(x.Quantity));
    }

    public async Task<ApiResponse<ProcurementGoodsReceiptDto>> CreateGoodsReceiptAsync(
        CreateProcurementGoodsReceiptRequest request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<ProcurementGoodsReceiptDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.PurchaseOrderId <= 0 ||
            request.LocationId <= 0 || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 10 or > 800 || request.Lines is not { Count: >= 1 and <= 100 } ||
            request.Lines.Any(x => x.PurchaseOrderLineId <= 0 || !ReceiptQty(x.Quantity) || x.LotOrBatchNo?.Trim().Length > 100) ||
            request.Lines.Select(x => (x.PurchaseOrderLineId, x.LotOrBatchNo?.Trim() ?? "")).Distinct().Count() != request.Lines.Count)
            return ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Approved PO, store, unique item/lot lines and reason (10-800 characters) required.");
        var reason = request.Reason.Trim();
        var hash = ReceiptCreateHash(request, reason);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.ProcurementGoodsReceipts.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, ct);
            if (existing != null)
            {
                if (existing.CreateFingerprint != hash) return ReceiptConflict("Request key used for a different delivery.");
                if (!await LocationsAllowed(null, existing.LocationId, ct)) return Denied<ProcurementGoodsReceiptDto>();
                return ApiResponse<ProcurementGoodsReceiptDto>.SuccessResponse(await ReceiptDtoAsync(existing, ct), "Existing delivery returned.");
            }
            if (!await LocationsAllowed(null, request.LocationId, ct)) return Denied<ProcurementGoodsReceiptDto>();
            var po = await db.ProcurementPurchaseOrders.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.PurchaseOrderId && !x.IsDeleted, ct);
            if (po == null || po.StateCode != "Approved" ||
                !await db.InventoryLocations.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id == request.LocationId && x.CampusId == po.CampusId && x.IsActive && !x.IsDeleted, ct) ||
                !await db.ProcurementSuppliers.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id == po.SupplierId && x.CampusId == po.CampusId && x.StateCode == "Approved" && !x.IsDeleted, ct))
                return ReceiptConflict("Approved campus PO, supplier and store required.");
            var poLineIds = request.Lines.Select(x => x.PurchaseOrderLineId).Distinct().ToArray();
            var approved = await db.ProcurementPurchaseOrderLines.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.PurchaseOrderId == po.Id && !x.IsDeleted &&
                poLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            if (approved.Count != poLineIds.Length || approved.Values.Any(x => x.Quantity <= 0m ||
                x.UnitPrice < 0m || !Precision(x.Quantity, 6) || !Precision(x.UnitPrice, 4)))
                return ReceiptConflict("Order lines missing or changed.");
            var itemIds = approved.Values.Select(x => x.InventoryItemId).Distinct().ToArray();
            if (await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                itemIds.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != itemIds.Length)
                return ReceiptConflict("Inactive stock item on purchase order.");
            var pending = await ReceiptCommittedQuantitiesAsync(po.Id, null, ct);
            var requested = request.Lines.GroupBy(x => x.PurchaseOrderLineId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            if (requested.Any(x => x.Value > approved[x.Key].Quantity ||
                pending.GetValueOrDefault(x.Key) + x.Value > approved[x.Key].Quantity))
                return ReceiptConflict("Delivery exceeds unreceived PO quantity.");
            var now = clock.GetUtcNow().UtcDateTime;
            var receipt = new ProcurementGoodsReceipt
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                PurchaseOrderId = po.Id, CampusId = po.CampusId, LocationId = request.LocationId,
                Reason = reason, CreateFingerprint = hash, CreatedAt = now, CreatedBy = user.UserId
            };
            db.ProcurementGoodsReceipts.Add(receipt);
            await db.SaveChangesAsync(ct);
            db.ProcurementGoodsReceiptLines.AddRange(request.Lines.Select((x, i) =>
            {
                var poLine = approved[x.PurchaseOrderLineId];
                return new ProcurementGoodsReceiptLine
                {
                    TenantId = user.TenantId, GoodsReceiptId = receipt.Id, LineNo = i + 1,
                    PurchaseOrderLineId = x.PurchaseOrderLineId, InventoryItemId = poLine.InventoryItemId,
                    LotOrBatchNo = x.LotOrBatchNo?.Trim() ?? "", Quantity = x.Quantity,
                    ApprovedPoQuantity = poLine.Quantity, ApprovedPoUnitPrice = poLine.UnitPrice,
                    CreatedAt = now, CreatedBy = user.UserId
                };
            }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<ProcurementGoodsReceiptDto>.SuccessResponse(await ReceiptDtoAsync(receipt, ct), "Delivery draft recorded; stock unchanged.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Procurement receipt create race"); return ReceiptConflict("Delivery create conflict; reload."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        { logger.LogWarning(ex, "Procurement receipt create SQL race"); return ReceiptConflict("Delivery create conflict; reload."); }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        { logger.LogWarning(ex, "Procurement receipt create wrapped SQL race"); return ReceiptConflict("Delivery create conflict; reload."); }
    }

    public async Task<ApiResponse<ProcurementGoodsReceiptDto>> SubmitGoodsReceiptAsync(
        Guid reference, SubmitProcurementGoodsReceiptRequest request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<ProcurementGoodsReceiptDto>();
        if (request == null || request.ClientRequestId == Guid.Empty)
            return ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Unique submission key required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var receipt = await db.ProcurementGoodsReceipts.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (receipt == null || !await LocationsAllowed(null, receipt.LocationId, ct))
                return ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Goods receipt not found.", 404);
            if (receipt.CreatedBy != user.UserId) return Denied<ProcurementGoodsReceiptDto>();
            if (receipt.SubmitRequestId == request.ClientRequestId && receipt.SubmitFingerprint == "submit-v1")
                return ApiResponse<ProcurementGoodsReceiptDto>.SuccessResponse(await ReceiptDtoAsync(receipt, ct), "Already submitted.");
            if (receipt.StateCode != "Draft" || !Version(receipt.RowVersion, request.RowVersion))
                return ReceiptConflict("Delivery state changed; reload.");
            var lines = await db.ProcurementGoodsReceiptLines.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.GoodsReceiptId == receipt.Id && !x.IsDeleted).ToListAsync(ct);
            if (lines.Count is < 1 or > 100 || !await ReceiptSourceValidAsync(receipt, lines, ct))
                return ReceiptConflict("Approved PO or source items changed.");
            var reserved = await ReceiptCommittedQuantitiesAsync(receipt.PurchaseOrderId, receipt.Id, ct);
            if (lines.GroupBy(x => x.PurchaseOrderLineId).Any(g =>
                reserved.GetValueOrDefault(g.Key) + g.Sum(x => x.Quantity) > g.First().ApprovedPoQuantity))
                return ReceiptConflict("Submitted deliveries exceed PO remaining quantity.");
            var now = clock.GetUtcNow().UtcDateTime;
            receipt.StateCode = "Submitted"; receipt.SubmitRequestId = request.ClientRequestId;
            receipt.SubmitFingerprint = "submit-v1"; receipt.SubmittedAtUtc = now;
            receipt.SubmittedByUserId = user.UserId; receipt.UpdatedAt = now; receipt.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<ProcurementGoodsReceiptDto>.SuccessResponse(await ReceiptDtoAsync(receipt, ct), "Delivery submitted to independent checker.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Procurement receipt submit race"); return ReceiptConflict("Submit conflict; reload."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        { logger.LogWarning(ex, "Procurement receipt submit SQL race"); return ReceiptConflict("Submit conflict; reload."); }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        { logger.LogWarning(ex, "Procurement receipt submit wrapped SQL race"); return ReceiptConflict("Submit conflict; reload."); }
    }

    public async Task<ApiResponse<ProcurementGoodsReceiptDto>> ReviewGoodsReceiptAsync(
        Guid reference, ReviewProcurementGoodsReceiptRequest request, CancellationToken ct = default)
    {
        if (!Postable || db.CurrentTenantId != user.TenantId) return Denied<ProcurementGoodsReceiptDto>();
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.Decision is not ("Approve" or "Reject") ||
            request.ValuationMode is not ("Automatic" or "ManualFinal" or "AutoPlusManual") ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800 ||
            request.Valuations == null || request.Valuations.Count > 100 ||
            request.Valuations.Any(x => x.LineNo <= 0 || !ReceiptPrice(x.Amount, true) || x.Reason?.Trim().Length > 800) ||
            request.Valuations.Select(x => x.LineNo).Distinct().Count() != request.Valuations.Count ||
            (request.Decision == "Reject" && request.Valuations.Count > 0) ||
            (request.ValuationMode == "Automatic" && request.Valuations.Count > 0))
            return ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Valid decision, costing policy and review reason (10–800 characters) required.");
        var reason = request.Reason.Trim();
        var hash = ReceiptReviewHash(request, reason);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var receipt = await db.ProcurementGoodsReceipts.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted, ct);
            if (receipt == null || !await LocationsAllowed(null, receipt.LocationId, ct))
                return ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Delivery not found.", 404);
            if (!receipt.CreatedBy.HasValue || receipt.CreatedBy == user.UserId || receipt.SubmittedByUserId == user.UserId)
                return Denied<ProcurementGoodsReceiptDto>();
            if (receipt.ReviewRequestId == request.ClientRequestId && receipt.ReviewFingerprint == hash)
                return ApiResponse<ProcurementGoodsReceiptDto>.SuccessResponse(await ReceiptDtoAsync(receipt, ct), "Already reviewed.");
            if (receipt.StateCode != "Submitted" || !Version(receipt.RowVersion, request.RowVersion))
                return ReceiptConflict("Delivery changed or already reviewed.");
            var lines = await db.ProcurementGoodsReceiptLines.Where(x => x.TenantId == user.TenantId &&
                x.GoodsReceiptId == receipt.Id && !x.IsDeleted).OrderBy(x => x.LineNo).ToListAsync(ct);
            if (lines.Count is < 1 or > 100) return ReceiptConflict("Missing delivered lines.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (request.Decision == "Approve")
            {
                if (!await ReceiptSourceValidAsync(receipt, lines, ct))
                    return ReceiptConflict("Purchase order, supplier, campus or item changed.");
                var committed = await ReceiptCommittedQuantitiesAsync(receipt.PurchaseOrderId, receipt.Id, ct);
                if (lines.GroupBy(x => x.PurchaseOrderLineId).Any(g =>
                    committed.GetValueOrDefault(g.Key) + g.Sum(x => x.Quantity) > g.First().ApprovedPoQuantity))
                    return ReceiptConflict("PO quantity exhausted by another accepted delivery.");
                var choices = request.Valuations.ToDictionary(x => x.LineNo);
                if (choices.Keys.Any(n => lines.All(x => x.LineNo != n)) ||
                    (request.ValuationMode == "ManualFinal" && choices.Count != lines.Count))
                    return ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Manual Final requires a unit cost for every delivered line.");
                var movementLines = new List<CreateInventoryMovementLineRequestDto>();
                var posted = new CreateInventoryMovementRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), MovementTypeCode = "Receipt",
                    ToLocationId = receipt.LocationId, SourceType = "ProcurementGoodsReceipt",
                    Remarks = "Approved procurement goods receipt " + receipt.PublicId + "; " + reason,
                    Lines = movementLines
                };
                foreach (var line in lines)
                {
                    choices.TryGetValue(line.LineNo, out var choice);
                    var unitCost = request.ValuationMode switch
                    {
                        "Automatic" => line.ApprovedPoUnitPrice,
                        "ManualFinal" => choice!.Amount,
                        _ => line.ApprovedPoUnitPrice + (choice?.Amount ?? 0m)
                    };
                    if (!ReceiptPrice(unitCost) || unitCost <= 0m)
                        return ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Positive received stock requires valid unit cost (four decimal places).");
                    if (unitCost != line.ApprovedPoUnitPrice && (choice == null ||
                        string.IsNullOrWhiteSpace(choice.Reason) || choice.Reason.Trim().Length is < 10 or > 800))
                        return ApiResponse<ProcurementGoodsReceiptDto>.ErrorResponse("Valuation override needs line reason (10–800 characters).");
                    line.PostedUnitCost = unitCost;
                    line.ValuationReason = unitCost == line.ApprovedPoUnitPrice ? null : choice!.Reason!.Trim();
                    movementLines.Add(new CreateInventoryMovementLineRequestDto
                    {
                        InventoryItemId = line.InventoryItemId, LotOrBatchNo = line.LotOrBatchNo,
                        Quantity = line.Quantity, UnitCost = unitCost
                    });
                }
                var (movement, _) = await PersistDraftAsync(posted, ct);
                movement.SourceId = receipt.Id;
                var (error, _) = await CheckPostingAsync(movement, ct);
                if (error != null) return ReceiptConflict("Receipt could not post: " + error);
                movement.StateCode = "Posted"; movement.PostedAt = now; movement.PostedByUserId = user.UserId;
                movement.UpdatedAt = now; movement.UpdatedBy = user.UserId;
                receipt.PostedMovementId = movement.Id;
            }
            receipt.StateCode = request.Decision == "Approve" ? "Posted" : "Rejected";
            receipt.ReviewRequestId = request.ClientRequestId; receipt.ReviewFingerprint = hash;
            receipt.ValuationMode = request.Decision == "Approve" ? request.ValuationMode : null;
            receipt.ReviewReason = reason; receipt.ReviewedAtUtc = now; receipt.ReviewedByUserId = user.UserId;
            receipt.UpdatedAt = now; receipt.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<ProcurementGoodsReceiptDto>.SuccessResponse(await ReceiptDtoAsync(receipt, ct),
                request.Decision == "Approve" ? "Goods receipt approved and stock posted atomically." : "Delivery rejected; stock unchanged.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Procurement receipt review race"); return ReceiptConflict("Review conflict; reload."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        { logger.LogWarning(ex, "Procurement receipt review SQL race"); return ReceiptConflict("Review conflict; reload."); }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        { logger.LogWarning(ex, "Procurement receipt review wrapped SQL race"); return ReceiptConflict("Review conflict; reload."); }
    }
}
