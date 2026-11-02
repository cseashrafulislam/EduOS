using System.Text;
using System.Text.Json;
using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/procurement/supplier-invoices"), Authorize, RequireModule("INVENTORY"), RequireModule("FINANCE"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementSupplierInvoicesController(IProcurementSupplierInvoiceService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet, Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> List(long? purchaseOrderId, string? state, int page=1, int pageSize=25,
        CancellationToken ct=default) => Reply(await service.ListAsync(purchaseOrderId,state,page,pageSize,ct));

    [HttpGet("options"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Options(CancellationToken ct) => Reply(await service.PostingOptionsAsync(ct));

    [HttpGet("{reference:guid}"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct) => Reply(await service.GetAsync(reference,ct));

    [HttpPost, Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> Create([FromBody] CreateProcurementSupplierInvoiceRequest request, CancellationToken ct)
        => Reply(await service.CreateAsync(request,ct));

    [HttpPost("{reference:guid}/submit"), Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> Submit(Guid reference,[FromBody] SubmitProcurementSupplierInvoiceRequest request,
        CancellationToken ct) => Reply(await service.SubmitAsync(reference,request,ct));

    [HttpPost("{reference:guid}/review"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Review(Guid reference,[FromBody] ReviewProcurementSupplierInvoiceRequest request,
        CancellationToken ct) => Reply(await service.ReviewAsync(reference,request,ct));

    [HttpPost("{reference:guid}/reversal"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> Reverse(Guid reference,[FromBody] RequestProcurementSupplierInvoiceReversal request,
        CancellationToken ct) => Reply(await service.RequestReversalAsync(reference,request,ct));

    [HttpPost("{reference:guid}/reversal/review"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> ReviewReversal(Guid reference,[FromBody] ReviewProcurementSupplierInvoiceReversal request,
        CancellationToken ct) => Reply(await service.ReviewReversalAsync(reference,request,ct));

    [HttpGet("{reference:guid}/returns"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Returns(Guid reference, int page=1, int pageSize=25,
        CancellationToken ct=default) => Reply(await service.ReturnCreditsAsync(reference,page,pageSize,ct));

    [HttpPost("{reference:guid}/returns"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestReturn(Guid reference,
        [FromBody] CreateSupplierReturnCreditRequest request, CancellationToken ct) =>
        Reply(await service.RequestReturnCreditAsync(reference,request,ct));

    [HttpPost("{reference:guid}/returns/{returnReference:guid}/review"), RequireModule("FINANCE"),
     Authorize(Roles="TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewReturn(Guid reference, Guid returnReference,
        [FromBody] ReviewSupplierReturnCreditRequest request, CancellationToken ct) =>
        Reply(await service.ReviewReturnCreditAsync(reference,returnReference,request,ct));

    // Canonical institutional Finance Journals. Posting records confirmed bank evidence;
    // it never calls the bank or initiates an electronic transfer.
    [HttpGet("{reference:guid}/settlements/options"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> SettlementOptions(Guid reference, CancellationToken ct)
        => Reply(await service.SettlementOptionsAsync(reference, ct));

    [HttpGet("{reference:guid}/settlements"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Settlements(Guid reference, int page=1, int pageSize=25, CancellationToken ct=default)
        => Reply(await service.SettlementInstructionsAsync(reference, page, pageSize, ct));

    [HttpPost("{reference:guid}/settlements"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestSettlement(Guid reference, [FromBody] CreateSupplierSettlementInstructionRequest request, CancellationToken ct)
        => Reply(await service.RequestSettlementAsync(reference, request, ct));

    [HttpPost("{reference:guid}/settlements/{instruction:guid}/withdraw"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> WithdrawSettlement(Guid reference, Guid instruction,
        [FromBody] WithdrawSupplierSettlementInstructionRequest request, CancellationToken ct)
        => Reply(await service.WithdrawSettlementAsync(reference, instruction, request, ct));

    [HttpPost("{reference:guid}/settlements/{instruction:guid}/review"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> ReviewSettlement(Guid reference, Guid instruction,
        [FromBody] ReviewSupplierSettlementInstructionRequest request, CancellationToken ct)
        => Reply(await service.ReviewSettlementAsync(reference, instruction, request, ct));

    [HttpPost("{reference:guid}/settlements/{instruction:guid}/post"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> PostSettlement(Guid reference, Guid instruction,
        [FromBody] PostSupplierSettlementRequest request, CancellationToken ct)
        => Reply(await service.PostSettlementAsync(reference, instruction, request, ct));

    [HttpPost("{reference:guid}/settlements/{instruction:guid}/reversal"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestSettlementReversal(Guid reference, Guid instruction,
        [FromBody] RequestSupplierSettlementReversal request, CancellationToken ct)
        => Reply(await service.RequestSettlementReversalAsync(reference, instruction, request, ct));

    [HttpPost("{reference:guid}/settlements/{instruction:guid}/reversal/{reversal:guid}/review"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> ReviewSettlementReversal(Guid reference, Guid instruction, Guid reversal,
        [FromBody] ReviewSupplierSettlementReversal request, CancellationToken ct)
        => Reply(await service.ReviewSettlementReversalAsync(reference, instruction, reversal, request, ct));

    // Reject duplicate/missing rows or changed visibility while a bounded CSV is
    // assembled across requests. This detects paging drift; it is not a SQL
    // snapshot-isolation guarantee against same-count replacement.
    private static bool AppendStablePage<T>(PagedResult<T> page, int expectedPage,
        int expectedTotal, List<T> output, HashSet<Guid> seen, Func<T, Guid> key)
    {
        if (page.TotalCount != expectedTotal || page.Page != expectedPage ||
            page.PageSize != 100 || page.Items == null ||
            page.Items.Count != Math.Min(100, expectedTotal - output.Count))
            return false;
        foreach (var item in page.Items)
        {
            if (item is null || !seen.Add(key(item)) || key(item) == Guid.Empty)
                return false;
        }
        output.AddRange(page.Items);
        return true;
    }

    private static string CsvCell(object? value)
    {
        var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        // Numeric financial values (including legitimate negative variances) must
        // remain numbers in spreadsheets. Untrusted TEXT beginning with a formula
        // operator is neutralized, including after leading whitespace.
        var numeric = value is sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal;
        s = s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        var trimmed = s.TrimStart();
        if (!numeric && trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    // A bounded second authorized read detects same-count replacements and
    // financial evidence changes missed by offset-page/duplicate-ID checks.
    // This is NOT a SQL snapshot guarantee: a later write can still race export.
    private async Task<IActionResult?> RecheckExportAsync<T>(
        IReadOnlyList<T> original, int total,
        Func<int, Task<ApiResponse<PagedResult<T>>>> readPage, Func<T, Guid> key)
    {
        var checkedRows = new List<T>();
        var seen = new HashSet<Guid>();
        for (var page = 1; page <= Math.Max(1, (total + 99) / 100); page++)
        {
            var response = await readPage(page);
            if (!response.Success) return Reply(response);
            if (response.Data == null ||
                !AppendStablePage(response.Data, page, total, checkedRows, seen, key))
                return StatusCode(409, new { message = "Supplier finance export changed; retry with fresh authorization." });
        }
        return checkedRows.Count == original.Count &&
            JsonSerializer.Serialize(checkedRows) == JsonSerializer.Serialize(original)
            ? null
            : StatusCode(409, new { message = "Supplier finance export changed; retry with fresh authorization." });
    }

    [HttpGet("aging"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Aging(DateOnly? dueDateReference,long? supplierId,bool overdueOnly=false,
        int page=1,int pageSize=25,CancellationToken ct=default)
        => Reply(await service.AgingAsync(dueDateReference,supplierId,overdueOnly,page,pageSize,ct));

    [HttpGet("aging/export"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> AgingExport(DateOnly? dueDateReference,long? supplierId,
        bool overdueOnly=false,CancellationToken ct=default)
    {
        var first=await service.AgingAsync(dueDateReference,supplierId,overdueOnly,1,100,ct);
        if (!first.Success) return Reply(first);
        if (first.Data == null) return StatusCode(409, new { message = "Supplier payable export changed; retry." });
        if(first.Data.TotalCount>500)return StatusCode(413,new{message="Filter to 500 or fewer supplier payables."});
        var rows=new List<ProcurementSupplierPayableAgingRowDto>();
        var seen=new HashSet<Guid>();
        var total=first.Data.TotalCount;
        if (!AppendStablePage(first.Data,1,total,rows,seen,x=>x.InvoiceReference))
            return StatusCode(409,new{message="Supplier payable export changed; retry with fresh authorization."});
        for(var p=2;p<=first.Data.TotalPages;p++)
        {
            var next=await service.AgingAsync(dueDateReference,supplierId,overdueOnly,p,100,ct);
            if (!next.Success) return Reply(next);
            if (next.Data == null) return StatusCode(409, new { message = "Supplier payable export changed; retry." });
            if (!AppendStablePage(next.Data,p,total,rows,seen,x=>x.InvoiceReference))
                return StatusCode(409,new{message="Supplier payable export changed; retry with fresh authorization."});
        }
        var agingDrift = await RecheckExportAsync(rows, total,
            p => service.AgingAsync(dueDateReference, supplierId, overdueOnly, p, 100, ct),
            x => x.InvoiceReference);
        if (agingDrift != null) return agingDrift;
        var csv=new StringBuilder("Invoice,PO,Supplier,Campus,DueDate,DaysOverdue,Bucket,GrossPostedAP_BDT,PostedSettlement_BDT,PostedReturnCredit_BDT,PendingReturnCredit_BDT,SupplierRefundReceivable_BDT,UnpostedReservation_BDT,SourceLinkedNet_BDT,SettlementEvidence,ReversalPending,Journal\r\n");
        foreach(var x in rows)
            csv.AppendJoin(',',CsvCell(x.SupplierInvoiceNumber),CsvCell(x.OrderNumber),CsvCell(x.SupplierId),
                CsvCell(x.CampusId),CsvCell(x.DueDate),CsvCell(x.DaysOverdue),CsvCell(x.AgingBucket),
                CsvCell(x.GrossPostedAmount),CsvCell(x.PostedSettlementAmount),
                CsvCell(x.PostedSupplierCreditAmount),CsvCell(x.PendingSupplierCreditAmount),
                CsvCell(x.SupplierRefundReceivableAmount),CsvCell(x.ReservedUnpostedAmount),
                CsvCell(x.SourceLinkedOutstandingAmount),CsvCell(x.SettlementIntegrityStatus),
                CsvCell(x.ReversalPending),CsvCell(x.JournalReference))
                .Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()),"text/csv; charset=utf-8",
            "supplier-source-linked-payable-aging.csv");
    }

    [HttpGet("export"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Export(long? purchaseOrderId, string? state, CancellationToken ct)
    {
        var first = await service.ListAsync(purchaseOrderId,state,1,100,ct);
        if (!first.Success) return Reply(first);
        if (first.Data == null) return StatusCode(409, new { message = "Supplier invoice export changed; retry." });
        if (first.Data.TotalCount > 500) return StatusCode(413,new{message="Filter to 500 or fewer supplier invoices."});
        var data = new List<ProcurementSupplierInvoiceDto>();
        var seen = new HashSet<Guid>();
        var total = first.Data.TotalCount;
        if (!AppendStablePage(first.Data,1,total,data,seen,x=>x.Reference))
            return StatusCode(409,new{message="Supplier invoice export changed; retry with fresh authorization."});
        for (var page=2; page<=first.Data.TotalPages;page++)
        {
            var next = await service.ListAsync(purchaseOrderId,state,page,100,ct);
            if (!next.Success) return Reply(next);
            if (next.Data == null) return StatusCode(409, new { message = "Supplier invoice export changed; retry." });
            if (!AppendStablePage(next.Data,page,total,data,seen,x=>x.Reference))
                return StatusCode(409,new{message="Supplier invoice export changed; retry with fresh authorization."});
        }
        var invoiceDrift = await RecheckExportAsync(data, total,
            p => service.ListAsync(purchaseOrderId, state, p, 100, ct),
            x => x.Reference);
        if (invoiceDrift != null) return invoiceDrift;
        var csv = new StringBuilder("Invoice,PO,Campus,Supplier,Date,DueDate,State,PriceMode,TotalBDT,StockValue,Variance,Maker,FinanceReviewer,Journal,Reversal,Item,Lot,Qty,ReceiptLine,POPrice,StockUnitCost,InvoiceUnitPrice,LineAmount,Reason\r\n");
        foreach (var x in data)
            foreach (var line in x.Lines)
                csv.AppendJoin(',',CsvCell(x.SupplierInvoiceNumber),CsvCell(x.OrderNumber),CsvCell(x.CampusId),CsvCell(x.SupplierId),
                    CsvCell(x.InvoiceDate),CsvCell(x.DueDate),CsvCell(x.StateCode),CsvCell(x.PriceMode),CsvCell(x.TotalAmount),
                    CsvCell(x.StockValue),CsvCell(x.PriceVariance),CsvCell(x.CreatedByUserId),CsvCell(x.ReviewedByUserId),
                    CsvCell(x.JournalReference),CsvCell(x.ReversalJournalReference),CsvCell(line.InventoryItemId),
                    CsvCell(line.LotOrBatchNo),CsvCell(line.Quantity),CsvCell(line.GoodsReceiptLineId),
                    CsvCell(line.PoUnitPrice),CsvCell(line.StockUnitCost),CsvCell(line.InvoiceUnitPrice),
                    CsvCell(line.InvoiceAmount),CsvCell(line.VarianceReason)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()),"text/csv; charset=utf-8",
            "procurement-supplier-invoices.csv");
    }
}
