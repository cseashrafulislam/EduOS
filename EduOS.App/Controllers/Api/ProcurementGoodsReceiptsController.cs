using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/procurement/goods-receipts"), Authorize,
 RequireModule("INVENTORY"), AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementGoodsReceiptsController(IInventoryMovementService inventory) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);

    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> List(long? purchaseOrderId, string? state, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
        => Reply(await inventory.GetGoodsReceiptsAsync(purchaseOrderId, state, page, pageSize, ct));

    [HttpGet("{reference:guid}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct)
        => Reply(await inventory.GetGoodsReceiptAsync(reference, ct));

    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Create([FromBody] CreateProcurementGoodsReceiptRequest request, CancellationToken ct)
        => Reply(await inventory.CreateGoodsReceiptAsync(request, ct));

    [HttpPost("{reference:guid}/submit"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Submit(Guid reference, [FromBody] SubmitProcurementGoodsReceiptRequest request,
        CancellationToken ct) => Reply(await inventory.SubmitGoodsReceiptAsync(reference, request, ct));

    [HttpPost("{reference:guid}/review"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> Review(Guid reference, [FromBody] ReviewProcurementGoodsReceiptRequest request,
        CancellationToken ct) => Reply(await inventory.ReviewGoodsReceiptAsync(reference, request, ct));

    // An empty or damaged line projection must never silently remove a received
    // item from a source-linked stock/Finance evidence download.
    // Match the canonical receipt posting contract: quantities use six decimals,
    // PO/posted unit prices four, and multiple lots of one PO line cannot exceed
    // the single approved quantity. This is DTO/export integrity, not a substitute
    // for the SQL transaction's source and stock checks.
    private static bool ReceiptQuantity(decimal value) =>
        value > 0m && value <= 999999999999m && decimal.Round(value, 6) == value;
    private static bool ReceiptUnitPrice(decimal value) =>
        value >= 0m && value <= 999999999999m && decimal.Round(value, 4) == value;

    private static bool HasCompleteReceiptLines(ProcurementGoodsReceiptDto receipt)
    {
        if (receipt.Reference == Guid.Empty || receipt.PurchaseOrderId <= 0 ||
            string.IsNullOrWhiteSpace(receipt.OrderNumber) ||
            receipt.CampusId <= 0 || receipt.LocationId <= 0 ||
            receipt.StateCode is not ("Draft" or "Submitted" or "Posted" or "Rejected") ||
            receipt.Lines is not { Count: >= 1 and <= 100 })
            return false;
        var posted = receipt.StateCode == "Posted";
        if (posted != (receipt.PostedMovementReference is { } movement && movement != Guid.Empty))
            return false;
        var lineNumbers = new HashSet<int>();
        foreach (var line in receipt.Lines)
        {
            if (line is null || line.LineNo <= 0 || !lineNumbers.Add(line.LineNo) ||
                line.PurchaseOrderLineId <= 0 || line.InventoryItemId <= 0 ||
                !ReceiptQuantity(line.Quantity) || !ReceiptQuantity(line.ApprovedPoQuantity) ||
                line.Quantity > line.ApprovedPoQuantity ||
                !ReceiptUnitPrice(line.ApprovedPoUnitPrice) ||
                (line.PostedUnitCost is { } cost && (!ReceiptUnitPrice(cost) || cost <= 0m)) ||
                (posted && line.PostedUnitCost is null) ||
                (!posted && line.PostedUnitCost is not null) ||
                (posted && line.PostedUnitCost != line.ApprovedPoUnitPrice &&
                 (string.IsNullOrWhiteSpace(line.ValuationReason) ||
                  line.ValuationReason.Trim().Length is < 10 or > 800)))
                return false;
        }
        // A receipt may legitimately split one approved PO line across lots.
        // Each lot must retain the same PO evidence, and their combined quantity
        // must not exceed that line's approved quantity.
        return receipt.Lines.GroupBy(x => x.PurchaseOrderLineId).All(group =>
            group.All(x => x.ApprovedPoQuantity == group.First().ApprovedPoQuantity &&
                x.ApprovedPoUnitPrice == group.First().ApprovedPoUnitPrice &&
                x.InventoryItemId == group.First().InventoryItemId) &&
            group.Sum(x => x.Quantity) <= group.First().ApprovedPoQuantity);
    }

    [HttpGet("export"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Export(long? purchaseOrderId, string? state, CancellationToken ct)
    {
        var (results, error) = await ProcurementCsvExportGuard.ReadAsync(
            page => inventory.GetGoodsReceiptsAsync(purchaseOrderId, state, page, 100, ct),
            (ProcurementGoodsReceiptDto x) => x.Reference);
        if (results == null)
            return StatusCode(error, new { message = "Procurement export changed or access denied; retry with fresh authorization and filters." });
        if (results.Any(receipt => !HasCompleteReceiptLines(receipt)))
            return StatusCode(409, new { message = "Goods receipt source lines unavailable; retry with fresh authorization." });
        var csv = new StringBuilder("Receipt UUID,Order,Campus,Location,Status,Maker,Checker,Line,Item,Lot,Quantity,PO Quantity,PO Unit Price,Posted Unit Cost,Valuation Reason,Inventory Movement UUID\r\n");
        foreach (var receipt in results)
            foreach (var line in receipt.Lines)
                csv.AppendJoin(',', ProcurementCsvExportGuard.Cell(receipt.Reference), ProcurementCsvExportGuard.Cell(receipt.OrderNumber), ProcurementCsvExportGuard.Cell(receipt.CampusId),
                    ProcurementCsvExportGuard.Cell(receipt.LocationId), ProcurementCsvExportGuard.Cell(receipt.StateCode), ProcurementCsvExportGuard.Cell(receipt.CreatedByUserId),
                    ProcurementCsvExportGuard.Cell(receipt.ReviewedByUserId), ProcurementCsvExportGuard.Cell(line.LineNo), ProcurementCsvExportGuard.Cell(line.InventoryItemId),
                    ProcurementCsvExportGuard.Cell(line.LotOrBatchNo), ProcurementCsvExportGuard.Cell(line.Quantity), ProcurementCsvExportGuard.Cell(line.ApprovedPoQuantity),
                    ProcurementCsvExportGuard.Cell(line.ApprovedPoUnitPrice), ProcurementCsvExportGuard.Cell(line.PostedUnitCost), ProcurementCsvExportGuard.Cell(line.ValuationReason),
                    ProcurementCsvExportGuard.Cell(receipt.PostedMovementReference)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", "procurement-goods-receipts.csv");
    }
}
