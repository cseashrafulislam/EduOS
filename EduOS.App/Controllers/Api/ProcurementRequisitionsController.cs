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
        var first = await service.ListAsync(1, 100, campusId, state, search, ct);
        if (!first.Success || first.Data == null) return StatusCode(first.StatusCode, first);
        if (first.Data.TotalCount > 500) return StatusCode(413, new { message = "Filter to 500 or fewer records before exporting." });
        var records = new List<ProcurementRequisitionDto>(first.Data.Items);
        for (var page = 2; page <= first.Data.TotalPages; page++)
        {
            var result = await service.ListAsync(page, 100, campusId, state, search, ct);
            if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
            records.AddRange(result.Data.Items);
        }
        static string Csv(object? value)
        {
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (text.TrimStart().Length > 0 && "=+-@".Contains(text.TrimStart()[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Number,Campus ID,State,Purpose,Requested By,Created UTC,Estimated BDT\r\n");
        foreach (var row in records)
            csv.AppendJoin(',', Csv(row.RequestNumber), Csv(row.CampusId), Csv(row.StateCode),
                Csv(row.Purpose), Csv(row.CreatedBy), Csv(row.CreatedAt.ToString("O")),
                Csv(row.EstimatedTotal)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "procurement-requisitions.csv");
    }
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);
}
