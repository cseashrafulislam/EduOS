using System.Text;
using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/facilities/work-orders"), Authorize, RequireModule("FACILITIES"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FacilityWorkOrdersApiController(IFacilityWorkflowService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician,FacilitiesRequester")]
    public async Task<IActionResult> List(long? campusId, string? state, int page = 1,
        int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.ListAsync(campusId, state, page, pageSize, ct));

    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician,FacilitiesRequester")]
    public async Task<IActionResult> Create([FromBody] CreateFacilityRequestDto request, CancellationToken ct) =>
        Reply(await service.CreateAsync(request, ct));

    [HttpPost("{id:long}/{action}"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician")]
    public async Task<IActionResult> Transition(long id, string action,
        [FromBody] FacilityTransitionDto command, CancellationToken ct) =>
        Reply(await service.TransitionAsync(id, action, command, ct));

    [HttpGet("{id:long}/history"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician,FacilitiesRequester")]
    public async Task<IActionResult> History(long id, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.HistoryAsync(id, page, pageSize, ct));

    [HttpGet("export"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician,FacilitiesRequester")]
    public async Task<IActionResult> Export(long? campusId, string? state, CancellationToken ct)
    {
        var first = await service.ListAsync(campusId, state, 1, 100, ct);
        if (!first.Success || first.Data == null) return Reply(first);
        if (first.Data.TotalCount > 500)
            return StatusCode(413, new { message = "Narrow filters to at most 500 rows." });
        var rows = new List<FacilityRequestDto>(first.Data.Items);
        for (var page = 2; page <= first.Data.TotalPages; page++)
        {
            var next = await service.ListAsync(campusId, state, page, 100, ct);
            if (!next.Success || next.Data == null) return Reply(next);
            rows.AddRange(next.Data.Items);
        }
        static string Csv(object? value)
        {
            var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (s.TrimStart().Length > 0 && "=+-@".Contains(s.TrimStart()[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Request,Campus,Space,Category,Priority,Summary,RequestState,WorkOrder,WorkState,Technician,SlaDueUtc,CompletedUtc,SlaBreached\r\n");
        foreach (var row in rows)
            csv.AppendJoin(',', Csv(row.Id), Csv(row.CampusId), Csv(row.FacilitySpaceId),
                Csv(row.CategoryCode), Csv(row.PriorityCode), Csv(row.Summary),
                Csv(row.StateCode), Csv(row.WorkOrderId), Csv(row.WorkOrderState),
                Csv(row.TechnicianUserId), Csv(row.SlaDueAtUtc), Csv(row.CompletedAtUtc),
                Csv(row.SlaBreached)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "facilities-work-orders.csv");
    }
}