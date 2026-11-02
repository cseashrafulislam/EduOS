using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/procurement/purchase-orders"), Authorize, RequireModule("INVENTORY"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementPurchaseOrdersController : ControllerBase
{
    private readonly IProcurementPurchaseOrderService service;
    public ProcurementPurchaseOrdersController(IProcurementPurchaseOrderService service) => this.service = service;
    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> List(int page = 1, int pageSize = 25, long? campusId = null,
        string? state = null, string? search = null, CancellationToken ct = default)
        => Reply(await service.ListAsync(page, pageSize, campusId, state, search, ct));
    [HttpGet("approvals"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> PendingApprovals(int page = 1, int pageSize = 25,
        long? campusId = null, string? search = null, CancellationToken ct = default)
        => Reply(await service.PendingApprovalsAsync(page, pageSize, campusId, search, ct));
    [HttpGet("commitments"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Commitments(int page = 1, int pageSize = 25,
        long? campusId = null, long? supplierId = null, DateTime? agingDate = null,
        bool overdueOnly = false, string? search = null, CancellationToken ct = default)
        => Reply(await service.CommitmentsAsync(page, pageSize, campusId, supplierId,
            agingDate, overdueOnly, search, ct));

    [HttpGet("commitments/export"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> ExportCommitments(long? campusId = null, long? supplierId = null,
        DateTime? agingDate = null, bool overdueOnly = false, string? search = null,
        CancellationToken ct = default)
    {
        var (records, error) = await ProcurementCsvExportGuard.ReadAsync(
            page => service.CommitmentsAsync(page, 100, campusId, supplierId, agingDate, overdueOnly, search, ct),
            (ProcurementPurchaseOrderCommitmentDto x) => x.Id);
        if (records == null)
            return StatusCode(error, new { message = "Procurement export changed or access denied; retry with fresh authorization and filters." });
        var csv = new StringBuilder("Order,Campus ID,Supplier ID,Supplier,Approval Status,Approved UTC,Expected Delivery,Aging Date,Schedule,Days Overdue,Lines,Currency,Approved Commitment,Supplier Confirmed UTC,Supplier Promised Delivery,Supplier Reference\r\n");
        foreach (var x in records)
            csv.AppendJoin(',', ProcurementCsvExportGuard.Cell(x.OrderNumber), ProcurementCsvExportGuard.Cell(x.CampusId), ProcurementCsvExportGuard.Cell(x.SupplierId),
                ProcurementCsvExportGuard.Cell(x.SupplierName), ProcurementCsvExportGuard.Cell(x.StateCode), ProcurementCsvExportGuard.Cell(x.ApprovedAt?.ToString("O")),
                ProcurementCsvExportGuard.Cell(x.ExpectedDeliveryDate?.ToString("yyyy-MM-dd")),
                ProcurementCsvExportGuard.Cell(x.AgingDate.ToString("yyyy-MM-dd")), ProcurementCsvExportGuard.Cell(x.DeliverySchedule),
                ProcurementCsvExportGuard.Cell(x.DaysOverdue), ProcurementCsvExportGuard.Cell(x.LineCount), ProcurementCsvExportGuard.Cell(x.CurrencyCode),
                ProcurementCsvExportGuard.Cell(x.ApprovedAmount), ProcurementCsvExportGuard.Cell(x.SupplierConfirmedAt?.ToString("O")),
                ProcurementCsvExportGuard.Cell(x.SupplierPromisedDeliveryDate?.ToString("yyyy-MM-dd")),
                ProcurementCsvExportGuard.Cell(x.SupplierConfirmationReference)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()),
            "text/csv; charset=utf-8", "procurement-approved-commitments.csv");
    }

    [HttpGet("{id:long}/history"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> History(long id, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
        => Reply(await service.HistoryAsync(id, page, pageSize, ct));
    [HttpGet("{id:long}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Get(long id, CancellationToken ct) => Reply(await service.GetAsync(id, ct));
    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Create([FromBody] CreateProcurementPurchaseOrderRequest request, CancellationToken ct)
        => Reply(await service.CreateAsync(request, ct));
    [HttpPut("{id:long}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> SaveDraft(long id,
        [FromBody] SaveProcurementPurchaseOrderDraftRequest request, CancellationToken ct)
        => Reply(await service.SaveDraftAsync(id, request, ct));
    [HttpPost("{id:long}/{action}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Transition(long id, string action,
        [FromBody] ProcurementPurchaseOrderDecisionRequest request, CancellationToken ct)
        => Reply(await service.TransitionAsync(id, action, request, ct));
    [HttpGet("export"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Export(long? campusId = null, string? state = null,
        string? search = null, CancellationToken ct = default)
    {
        var (records, error) = await ProcurementCsvExportGuard.ReadAsync(
            page => service.ListAsync(page, 100, campusId, state, search, ct),
            (ProcurementPurchaseOrderDto x) => x.Id);
        if (records == null)
            return StatusCode(error, new { message = "Procurement export changed or access denied; retry with fresh authorization and filters." });
        var csv = new StringBuilder("Order,Campus ID,Requisition ID,Supplier ID,Status,Currency,Amount,Created UTC\r\n");
        foreach (var x in records)
            csv.AppendJoin(',', ProcurementCsvExportGuard.Cell(x.OrderNumber), ProcurementCsvExportGuard.Cell(x.CampusId), ProcurementCsvExportGuard.Cell(x.RequisitionId),
                ProcurementCsvExportGuard.Cell(x.SupplierId), ProcurementCsvExportGuard.Cell(x.StateCode), ProcurementCsvExportGuard.Cell(x.CurrencyCode), ProcurementCsvExportGuard.Cell(x.TotalAmount),
                ProcurementCsvExportGuard.Cell(x.CreatedAt.ToString("O"))).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "procurement-purchase-orders.csv");
    }
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);
}
