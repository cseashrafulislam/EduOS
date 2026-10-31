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
        var first = await service.ListAsync(1, 100, campusId, state, ct);
        if (!first.Success || first.Data == null) return StatusCode(first.StatusCode, first);
        if (first.Data.TotalCount > 500) return StatusCode(413, new { message = "Filter to 500 or fewer confirmations." });
        var rows = new List<ProcurementSupplierAcknowledgementDto>(first.Data.Items);
        for (var page = 2; page <= first.Data.TotalPages; page++)
        {
            var next = await service.ListAsync(page, 100, campusId, state, ct);
            if (!next.Success || next.Data == null) return StatusCode(next.StatusCode, next);
            rows.AddRange(next.Data.Items);
        }
        static string Csv(object? value)
        {
            var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (s.TrimStart().Length > 0 && "=+-@".Contains(s.TrimStart()[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Order,Campus,Supplier,Reference,Response Date,Promised Delivery,State,Checker UTC\r\n");
        foreach (var x in rows)
            csv.AppendJoin(',', Csv(x.OrderNumber), Csv(x.CampusId), Csv(x.SupplierId),
                Csv(x.SupplierReference), Csv(x.ResponseDate.ToString("yyyy-MM-dd")),
                Csv(x.PromisedDeliveryDate.ToString("yyyy-MM-dd")), Csv(x.StateCode),
                Csv(x.DecidedAt?.ToString("O"))).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "procurement-supplier-confirmations.csv");
    }
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);
}
