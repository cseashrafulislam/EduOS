using System.Text;
using System.Globalization;
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
    public async Task<IActionResult> List(long? campusId, string? state, bool overdueOnly = false,
        int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.ListAsync(campusId, state, page, pageSize, ct, overdueOnly));

    [HttpGet("locations"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician,FacilitiesRequester")]
    public async Task<IActionResult> Locations(string? search = null, int page = 1,
        int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.LocationsAsync(search, page, pageSize, ct));

    [HttpGet("technicians"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Technicians(long campusId, string? search = null,
        int page = 1, int pageSize = 25, DateTime? startUtc = null, DateTime? endUtc = null,
        long? workOrderId = null, CancellationToken ct = default) =>
        Reply(await service.TechniciansAsync(campusId, search, page, pageSize,
            ct, startUtc, endUtc, workOrderId));

    [HttpGet("vendors"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Vendors(long campusId, string? search = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default,
        long? workOrderId = null, DateTime? startUtc = null, DateTime? endUtc = null) =>
        Reply(await service.VendorsAsync(campusId, search, page, pageSize, ct,
            workOrderId, startUtc, endUtc));

    [HttpGet("dashboard"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician,FacilitiesRequester")]
    public async Task<IActionResult> Dashboard(long? campusId, CancellationToken ct) =>
        Reply(await service.DashboardAsync(campusId, ct));

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
    public async Task<IActionResult> Export(long? campusId, string? state, bool overdueOnly = false,
        CancellationToken ct = default)
    {
        var first = await service.ListAsync(campusId, state, 1, 100, ct, overdueOnly);
        if (!first.Success || first.Data == null) return Reply(first);
        var bangla = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bn";
        if (first.Data.TotalCount > 500)
            return StatusCode(413, new { message = bangla
                ? "সর্বোচ্চ ৫০০টি সারির মধ্যে আনতে ফিল্টার সংকুচিত করুন।"
                : "Narrow filters to at most 500 rows." });
        // Offset paging can change during a concurrent edit. Fail closed on
        // observable count/identity drift instead of exporting duplicate or
        // silently incomplete maintenance records as an authoritative report.
        var expectedCount = first.Data.TotalCount;
        var rows = new List<FacilityRequestDto>(first.Data.Items);
        var seen = new HashSet<long>();
        var inconsistent = first.Data.Page != 1 || first.Data.PageSize != 100 ||
            rows.Count > expectedCount || rows.Any(x => !seen.Add(x.Id));
        for (var page = 2; !inconsistent && page <= first.Data.TotalPages; page++)
        {
            var next = await service.ListAsync(campusId, state, page, 100, ct, overdueOnly);
            if (!next.Success || next.Data == null) return Reply(next);
            inconsistent = next.Data.TotalCount != expectedCount ||
                next.Data.Page != page || next.Data.PageSize != 100 ||
                rows.Count + next.Data.Items.Count > 500 ||
                next.Data.Items.Any(x => !seen.Add(x.Id));
            if (!inconsistent) rows.AddRange(next.Data.Items);
        }
        if (inconsistent || rows.Count != expectedCount)
            return StatusCode(409, new { message = bangla
                ? "রিপোর্ট তৈরির সময় তথ্য পরিবর্তিত হয়েছে। আবার চেষ্টা করুন।"
                : "Records changed during export; retry." });
        static string Csv(object? value)
        {
            var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (s.TrimStart().Length > 0 && "=+-@".Contains(s.TrimStart()[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder(bangla
            ? "অনুরোধ,ক্যাম্পাস,স্থাপনা,বিভাগ,অগ্রাধিকার,সারাংশ,অনুরোধের অবস্থা,কাজের আদেশ,কাজের অবস্থা,টেকনিশিয়ান,এসএলএ সময়সীমা (UTC),সম্পন্নের সময় (UTC),এসএলএ লঙ্ঘন,যাচাইকৃত বন্ধের সময় (UTC)\r\n"
            : "Request,Campus,Space,Category,Priority,Summary,RequestState,WorkOrder,WorkState,Technician,SlaDueUtc,CompletedUtc,SlaBreached,VerifiedClosedUtc\r\n");
        foreach (var row in rows)
            csv.AppendJoin(',', Csv(row.Id), Csv(row.CampusId), Csv(row.FacilitySpaceId),
                Csv(row.CategoryCode), Csv(row.PriorityCode), Csv(row.Summary),
                Csv(row.StateCode), Csv(row.WorkOrderId), Csv(row.WorkOrderState),
                Csv(row.TechnicianUserId), Csv(row.SlaDueAtUtc), Csv(row.CompletedAtUtc),
                Csv(row.SlaBreached), Csv(row.VerifiedAtUtc)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "facilities-work-orders.csv");
    }
}