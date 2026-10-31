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

    [HttpGet("export"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Export(long? purchaseOrderId, string? state, CancellationToken ct)
    {
        var first = await inventory.GetGoodsReceiptsAsync(purchaseOrderId, state, 1, 100, ct);
        if (!first.Success || first.Data == null) return Reply(first);
        if (first.Data.TotalCount > 500)
            return StatusCode(413, new { message = "Filter to 500 or fewer goods receipts." });
        var results = new List<ProcurementGoodsReceiptDto>(first.Data.Items);
        for (var page = 2; page <= first.Data.TotalPages; page++)
        {
            var next = await inventory.GetGoodsReceiptsAsync(purchaseOrderId, state, page, 100, ct);
            if (!next.Success || next.Data == null) return Reply(next);
            results.AddRange(next.Data.Items);
        }
        static string Csv(object? input)
        {
            var value = Convert.ToString(input, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (input is string && value.TrimStart().Length > 0 && "=+-@".Contains(value.TrimStart()[0])) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Receipt UUID,Order,Campus,Location,Status,Maker,Checker,Line,Item,Lot,Quantity,PO Quantity,PO Unit Price,Posted Unit Cost,Valuation Reason,Inventory Movement UUID\r\n");
        foreach (var receipt in results)
            foreach (var line in receipt.Lines)
                csv.AppendJoin(',', Csv(receipt.Reference), Csv(receipt.OrderNumber), Csv(receipt.CampusId),
                    Csv(receipt.LocationId), Csv(receipt.StateCode), Csv(receipt.CreatedByUserId),
                    Csv(receipt.ReviewedByUserId), Csv(line.LineNo), Csv(line.InventoryItemId),
                    Csv(line.LotOrBatchNo), Csv(line.Quantity), Csv(line.ApprovedPoQuantity),
                    Csv(line.ApprovedPoUnitPrice), Csv(line.PostedUnitCost), Csv(line.ValuationReason),
                    Csv(receipt.PostedMovementReference)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", "procurement-goods-receipts.csv");
    }
}
