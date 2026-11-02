using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/procurement/supplier-confirmations"), Authorize, RequireModule("INVENTORY"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementSupplierAcknowledgementsController : ControllerBase
{
    private readonly IProcurementSupplierAcknowledgementService service;
    public ProcurementSupplierAcknowledgementsController(IProcurementSupplierAcknowledgementService service)
        => this.service = service;
    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> List(int page = 1, int pageSize = 25, long? campusId = null,
        string? state = null, CancellationToken ct = default)
        => Reply(await service.ListAsync(page, pageSize, campusId, state, ct));
    [HttpGet("{id:long}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => Reply(await service.GetAsync(id, ct));
    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Create([FromBody] SaveProcurementSupplierAcknowledgementRequest request,
        CancellationToken ct) => Reply(await service.SaveAsync(null, request, ct));
    [HttpPut("{id:long}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Update(long id,
        [FromBody] SaveProcurementSupplierAcknowledgementRequest request, CancellationToken ct)
        => Reply(await service.SaveAsync(id, request, ct));
    [HttpPost("{id:long}/{action}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Transition(long id, string action,
        [FromBody] ProcurementSupplierAcknowledgementDecisionRequest request, CancellationToken ct)
        => Reply(await service.TransitionAsync(id, action, request, ct));
    [HttpGet("export"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Export(long? campusId = null, string? state = null,
        CancellationToken ct = default)
    {
        var (rows, error) = await ProcurementCsvExportGuard.ReadAsync(
            page => service.ListAsync(page, 100, campusId, state, ct),
            (ProcurementSupplierAcknowledgementDto x) => x.Id);
        if (rows == null)
            return StatusCode(error, new { message = "Procurement export changed or access denied; retry with fresh authorization and filters." });
        var csv = new StringBuilder("Order,Campus,Supplier,Reference,Response Date,Promised Delivery,State,Checker UTC\r\n");
        foreach (var x in rows)
            csv.AppendJoin(',', ProcurementCsvExportGuard.Cell(x.OrderNumber), ProcurementCsvExportGuard.Cell(x.CampusId), ProcurementCsvExportGuard.Cell(x.SupplierId),
                ProcurementCsvExportGuard.Cell(x.SupplierReference), ProcurementCsvExportGuard.Cell(x.ResponseDate.ToString("yyyy-MM-dd")),
                ProcurementCsvExportGuard.Cell(x.PromisedDeliveryDate.ToString("yyyy-MM-dd")), ProcurementCsvExportGuard.Cell(x.StateCode),
                ProcurementCsvExportGuard.Cell(x.DecidedAt?.ToString("O"))).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "procurement-supplier-confirmations.csv");
    }
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);
}
