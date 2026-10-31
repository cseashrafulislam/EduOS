using System.Text;
using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/procurement/supplier-invoices"), Authorize, RequireModule("INVENTORY"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementSupplierInvoicesController(IProcurementSupplierInvoiceService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet, Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> List(long? purchaseOrderId, string? state, int page=1, int pageSize=25,
        CancellationToken ct=default) => Reply(await service.ListAsync(purchaseOrderId,state,page,pageSize,ct));

    [HttpGet("options"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Options(CancellationToken ct) => Reply(await service.PostingOptionsAsync(ct));

    [HttpGet("{reference:guid}"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct) => Reply(await service.GetAsync(reference,ct));

    [HttpPost, Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> Create([FromBody] CreateProcurementSupplierInvoiceRequest request, CancellationToken ct)
        => Reply(await service.CreateAsync(request,ct));

    [HttpPost("{reference:guid}/submit"), Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> Submit(Guid reference,[FromBody] SubmitProcurementSupplierInvoiceRequest request,
        CancellationToken ct) => Reply(await service.SubmitAsync(reference,request,ct));

    [HttpPost("{reference:guid}/review"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Review(Guid reference,[FromBody] ReviewProcurementSupplierInvoiceRequest request,
        CancellationToken ct) => Reply(await service.ReviewAsync(reference,request,ct));

    [HttpPost("{reference:guid}/reversal"), Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> Reverse(Guid reference,[FromBody] RequestProcurementSupplierInvoiceReversal request,
        CancellationToken ct) => Reply(await service.RequestReversalAsync(reference,request,ct));

    [HttpPost("{reference:guid}/reversal/review"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> ReviewReversal(Guid reference,[FromBody] ReviewProcurementSupplierInvoiceReversal request,
        CancellationToken ct) => Reply(await service.ReviewReversalAsync(reference,request,ct));

    [HttpGet("export"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Export(long? purchaseOrderId, string? state, CancellationToken ct)
    {
        var first = await service.ListAsync(purchaseOrderId,state,1,100,ct);
        if (!first.Success || first.Data == null) return Reply(first);
        if (first.Data.TotalCount > 500) return StatusCode(413,new{message="Filter to 500 or fewer supplier invoices."});
        var data = new List<ProcurementSupplierInvoiceDto>(first.Data.Items);
        for (var page=2; page<=first.Data.TotalPages;page++)
        {
            var next = await service.ListAsync(purchaseOrderId,state,page,100,ct);
            if (!next.Success || next.Data == null) return Reply(next);
            data.AddRange(next.Data.Items);
        }
        static string Csv(object? value)
        {
            var s = Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";
            if (s.TrimStart().Length>0 && "=+-@".Contains(s.TrimStart()[0])) s="'"+s;
            return "\""+s.Replace("\"","\"\"")+"\"";
        }
        var csv = new StringBuilder("Invoice,PO,Campus,Supplier,Date,DueDate,State,PriceMode,TotalBDT,StockValue,Variance,Maker,FinanceReviewer,Journal,Reversal,Item,Lot,Qty,ReceiptLine,POPrice,StockUnitCost,InvoiceUnitPrice,LineAmount,Reason\r\n");
        foreach (var x in data)
            foreach (var line in x.Lines)
                csv.AppendJoin(',',Csv(x.SupplierInvoiceNumber),Csv(x.OrderNumber),Csv(x.CampusId),Csv(x.SupplierId),
                    Csv(x.InvoiceDate),Csv(x.DueDate),Csv(x.StateCode),Csv(x.PriceMode),Csv(x.TotalAmount),
                    Csv(x.StockValue),Csv(x.PriceVariance),Csv(x.CreatedByUserId),Csv(x.ReviewedByUserId),
                    Csv(x.JournalReference),Csv(x.ReversalJournalReference),Csv(line.InventoryItemId),
                    Csv(line.LotOrBatchNo),Csv(line.Quantity),Csv(line.GoodsReceiptLineId),
                    Csv(line.PoUnitPrice),Csv(line.StockUnitCost),Csv(line.InvoiceUnitPrice),
                    Csv(line.InvoiceAmount),Csv(line.VarianceReason)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()),"text/csv; charset=utf-8",
            "procurement-supplier-invoices.csv");
    }
}
