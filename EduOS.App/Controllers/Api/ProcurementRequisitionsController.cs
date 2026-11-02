using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/procurement/requisitions"), Authorize, RequireModule("INVENTORY"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementRequisitionsController : ControllerBase
{
    private readonly IProcurementRequisitionService service;
    public ProcurementRequisitionsController(IProcurementRequisitionService service) => this.service = service;
    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> List(int page = 1, int pageSize = 25, long? campusId = null,
        string? state = null, string? search = null, CancellationToken ct = default)
        => Reply(await service.ListAsync(page, pageSize, campusId, state, search, ct));
    [HttpGet("pending-approvals"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> PendingApprovals(int page = 1, int pageSize = 25,
        long? campusId = null, string? search = null, CancellationToken ct = default)
        => Reply(await service.PendingApprovalsAsync(page, pageSize, campusId, search, ct));
    [HttpGet("{id:long}/history"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> History(long id, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.HistoryAsync(id, page, pageSize, ct));
    [HttpGet("{id:long}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Get(long id, CancellationToken ct) => Reply(await service.GetAsync(id, ct));
    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Create([FromBody] SaveProcurementRequisitionRequest request, CancellationToken ct)
        => Reply(await service.SaveAsync(null, request, ct));
    [HttpPut("{id:long}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveProcurementRequisitionRequest request, CancellationToken ct)
        => Reply(await service.SaveAsync(id, request, ct));
    [HttpPost("{id:long}/{action}"),
     Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Transition(long id, string action, [FromBody] ProcurementDecisionRequest request,
        CancellationToken ct) => Reply(await service.TransitionAsync(id, action, request, ct));

    // Deliberately bounded export; caller must narrow filters if >500 records.
    [HttpGet("export"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Export(long? campusId = null, string? state = null, string? search = null,
        CancellationToken ct = default)
    {
        var (records, error) = await ProcurementCsvExportGuard.ReadAsync(
            page => service.ListAsync(page, 100, campusId, state, search, ct),
            (ProcurementRequisitionDto x) => x.Id);
        if (records == null)
            return StatusCode(error, new { message = "Procurement export changed or access denied; retry with fresh authorization and filters." });
        var csv = new StringBuilder("Number,Campus ID,State,Purpose,Requested By,Created UTC,Estimated BDT\r\n");
        foreach (var row in records)
            csv.AppendJoin(',', ProcurementCsvExportGuard.Cell(row.RequestNumber), ProcurementCsvExportGuard.Cell(row.CampusId), ProcurementCsvExportGuard.Cell(row.StateCode),
                ProcurementCsvExportGuard.Cell(row.Purpose), ProcurementCsvExportGuard.Cell(row.CreatedBy), ProcurementCsvExportGuard.Cell(row.CreatedAt.ToString("O")),
                ProcurementCsvExportGuard.Cell(row.EstimatedTotal)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "procurement-requisitions.csv");
    }
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);
}
