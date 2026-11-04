using System.Data;
using System.Text;
using System.Text.Json;
using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/procurement/supplier-invoices"), Authorize, RequireModule("INVENTORY"), RequireModule("FINANCE"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementSupplierInvoicesController(
    IProcurementSupplierInvoiceService service, EduOSDbContext? db = null) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    // The real request's scoped DbContext is also used by the supplier invoice
    // service. Hold one bounded SQL Server SERIALIZABLE view across every page,
    // source-journal check and Finance CSV before making the file public.
    // The optional context preserves pure controller-contract test construction;
    // runtime DI supplies the registered scoped context.
    private async Task<IActionResult> FinanceExportSnapshotAsync(
        Func<Task<IActionResult>> read, CancellationToken ct)
    {
        if (db == null) return await read();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await read();
            if (result is FileContentResult) await tx.CommitAsync(ct);
            return result;
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 3960 or -2)
        {
            return StatusCode(409, new { message = "Supplier finance snapshot was busy; retry the export." });
        }
    }

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

    // Shared sanitization keeps Procurement and Supplier Finance CSV behavior identical.
    private static string CsvCell(object? value) => ProcurementCsvExportGuard.Cell(value);

    private IActionResult ExportFailure<T>(ApiResponse<T> response)
    {
        // Never echo service exception, bank evidence or private campus details
        // after an export read fails or its authorization is revoked.
        var status = response.StatusCode is >= 400 and <= 599 ? response.StatusCode : 409;
        return StatusCode(status, new { message = "Supplier finance export unavailable or access changed; retry with current authorization." });
    }

    // A missing, duplicated or incomplete invoice source-line projection must
    // never be silently omitted from a financial CSV, even for a draft invoice.
    private static bool ValidMoney(decimal value) =>
        value >= 0m && value <= 999999999999.99m && decimal.Round(value, 2) == value;

    // Validate source-linked CSV evidence without changing canonical posting.
    private static bool HasCompleteInvoiceLines(ProcurementSupplierInvoiceDto invoice)
    {
        if (invoice.PurchaseOrderId <= 0 || invoice.CampusId <= 0 || invoice.SupplierId <= 0 ||
            invoice.Lines is not { Count: >= 1 and <= 100 } ||
            !ValidMoney(invoice.TotalAmount) || !ValidMoney(invoice.StockValue) ||
            invoice.PriceVariance != invoice.TotalAmount - invoice.StockValue ||
            (invoice.StateCode == "Posted" &&
             (invoice.JournalReference is null || invoice.JournalReference == Guid.Empty)))
            return false;
        var numbers = new HashSet<int>();
        var receipts = new HashSet<long>();
        decimal billed = 0m, stock = 0m;
        foreach (var line in invoice.Lines)
        {
            if (line == null || line.Id <= 0 || line.LineNo <= 0 ||
                !numbers.Add(line.LineNo) || line.InventoryItemId <= 0 ||
                line.PurchaseOrderLineId <= 0 || line.GoodsReceiptLineId <= 0 ||
                !receipts.Add(line.GoodsReceiptLineId) ||
                line.Quantity <= 0m || line.Quantity > 999999999999m ||
                decimal.Round(line.Quantity, 6) != line.Quantity ||
                line.ReceivedQuantity < line.Quantity || line.ReceivedQuantity > 999999999999m ||
                decimal.Round(line.ReceivedQuantity, 6) != line.ReceivedQuantity ||
                line.PoUnitPrice < 0m || line.StockUnitCost < 0m ||
                line.InvoiceUnitPrice <= 0m ||
                line.PoUnitPrice > 999999999999.99m ||
                line.StockUnitCost > 999999999999.99m ||
                line.InvoiceUnitPrice > 999999999999.99m ||
                !ValidMoney(line.InvoiceAmount) || !ValidMoney(line.StockValue) ||
                decimal.Round(line.Quantity * line.InvoiceUnitPrice, 2, MidpointRounding.AwayFromZero) != line.InvoiceAmount ||
                decimal.Round(line.Quantity * line.StockUnitCost, 2, MidpointRounding.AwayFromZero) != line.StockValue)
                return false;
            billed += line.InvoiceAmount;
            stock += line.StockValue;
        }
        return billed == invoice.TotalAmount && stock == invoice.StockValue;
    }

    // Freeze the first authorized read BEFORE rechecking. Services or caches
    // may reuse mutable DTO instances across both reads; comparing two live
    // lists would incorrectly accept an in-place financial mutation. Runtime
    // exports additionally use a bounded serializable database transaction.
    private async Task<IActionResult?> RecheckExportAsync<T>(
        string frozenSignature, int total,
        Func<int, Task<ApiResponse<PagedResult<T>>>> readPage, Func<T, Guid> key)
    {
        var checkedRows = new List<T>();
        var seen = new HashSet<Guid>();
        for (var page = 1; page <= Math.Max(1, (total + 99) / 100); page++)
        {
            var response = await readPage(page);
            if (!response.Success) return ExportFailure(response);
            if (response.Data == null ||
                !AppendStablePage(response.Data, page, total, checkedRows, seen, key))
                return StatusCode(409, new { message = "Supplier finance export changed; retry with fresh authorization." });
        }
        return checkedRows.Count == total &&
            string.Equals(JsonSerializer.Serialize(checkedRows), frozenSignature, StringComparison.Ordinal)
            ? null
            : StatusCode(409, new { message = "Supplier finance export changed; retry with fresh authorization." });
    }

    [HttpGet("aging"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Aging(DateOnly? dueDateReference,long? supplierId,bool overdueOnly=false,
        int page=1,int pageSize=25,CancellationToken ct=default)
        => Reply(await service.AgingAsync(dueDateReference,supplierId,overdueOnly,page,pageSize,ct));

    [HttpGet("aging/export"), RequireModule("FINANCE"), Authorize(Roles="TenantAdmin,Principal,Accountant")]
    public Task<IActionResult> AgingExport(DateOnly? dueDateReference,long? supplierId,
        bool overdueOnly=false,CancellationToken ct=default) => FinanceExportSnapshotAsync(
            () => AgingExportCore(dueDateReference,supplierId,overdueOnly,ct),ct);

    private async Task<IActionResult> AgingExportCore(DateOnly? dueDateReference,long? supplierId,
        bool overdueOnly,CancellationToken ct)
    {
        var first=await service.AgingAsync(dueDateReference,supplierId,overdueOnly,1,100,ct);
        if (!first.Success) return ExportFailure(first);
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
            if (!next.Success) return ExportFailure(next);
            if (next.Data == null) return StatusCode(409, new { message = "Supplier payable export changed; retry." });
            if (!AppendStablePage(next.Data,p,total,rows,seen,x=>x.InvoiceReference))
                return StatusCode(409,new{message="Supplier payable export changed; retry with fresh authorization."});
        }
        var agingSignature = JsonSerializer.Serialize(rows);
        var agingDrift = await RecheckExportAsync(agingSignature, total,
            p => service.AgingAsync(dueDateReference, supplierId, overdueOnly, p, 100, ct),
            x => x.InvoiceReference);
        if (agingDrift != null) return agingDrift;
        // Render detached first-pass evidence, never mutable cached service DTOs.
        var frozenAging = ProcurementCsvExportGuard.Freeze<ProcurementSupplierPayableAgingRowDto>(agingSignature);
        if (frozenAging is null || frozenAging.Count != total)
            return StatusCode(409, new { message = "Supplier payable export changed; retry." });
        rows = frozenAging;
        // Never publish a financially authoritative aging CSV when a linked
        // settlement/return journal is corrupt, a source reference is missing,
        // or the projected net no longer reconciles to posted source amounts.
        // Service readback may intentionally display EvidenceConflict for
        // investigation; an exported spreadsheet must fail closed instead.
        if (rows.Any(x => x.InvoiceReference == Guid.Empty || x.JournalReference == Guid.Empty ||
            x.SupplierId <= 0 || x.CampusId <= 0 || x.DueDate == default ||
            x.SettlementIntegrityStatus != "SourceLinked" ||
            x.SourceLinkedOutstandingAmount is not { } outstanding ||
            x.GrossPostedAmount <= 0m || x.PostedSettlementAmount < 0m ||
            x.PostedSupplierCreditAmount < 0m || x.PendingSupplierCreditAmount < 0m ||
            x.ReservedUnpostedAmount < 0m || x.SupplierRefundReceivableAmount < 0m ||
            x.SupplierRefundReceivableAmount > x.PostedSupplierCreditAmount ||
            outstanding != Math.Max(0m, x.GrossPostedAmount -
                x.PostedSettlementAmount - x.PostedSupplierCreditAmount)))
            return StatusCode(409, new { message = "Supplier payable evidence conflict; reconcile source journals before export." });
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
    public Task<IActionResult> Export(long? purchaseOrderId, string? state, CancellationToken ct) =>
        FinanceExportSnapshotAsync(() => ExportCore(purchaseOrderId,state,ct),ct);

    private async Task<IActionResult> ExportCore(long? purchaseOrderId, string? state, CancellationToken ct)
    {
        var first = await service.ListAsync(purchaseOrderId,state,1,100,ct);
        if (!first.Success) return ExportFailure(first);
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
            if (!next.Success) return ExportFailure(next);
            if (next.Data == null) return StatusCode(409, new { message = "Supplier invoice export changed; retry." });
            if (!AppendStablePage(next.Data,page,total,data,seen,x=>x.Reference))
                return StatusCode(409,new{message="Supplier invoice export changed; retry with fresh authorization."});
        }
        if (data.Any(x => !HasCompleteInvoiceLines(x)))
            return StatusCode(409, new { message = "Supplier invoice source lines unavailable; retry with fresh authorization." });
        var invoiceSignature = JsonSerializer.Serialize(data);
        var invoiceDrift = await RecheckExportAsync(invoiceSignature, total,
            p => service.ListAsync(purchaseOrderId, state, p, 100, ct),
            x => x.Reference);
        if (invoiceDrift != null) return invoiceDrift;
        var frozenInvoices = ProcurementCsvExportGuard.Freeze<ProcurementSupplierInvoiceDto>(invoiceSignature);
        if (frozenInvoices is null || frozenInvoices.Count != total)
            return StatusCode(409, new { message = "Supplier invoice export changed; retry." });
        data = frozenInvoices;
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
