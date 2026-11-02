using System.Globalization;
using System.Text;
using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/facilities/visitors"), Authorize, RequireModule("FACILITIES"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FacilityVisitorsApiController(IFacilityVisitorService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet]
    public async Task<IActionResult> List(long? campusId, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.ListAsync(campusId, page, pageSize, ct));

    [HttpGet("hosts"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Hosts(long campusId, string? search = null, int page = 1,
        int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.HostsAsync(campusId, search, page, pageSize, ct));

    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Create([FromBody] CreateFacilityVisitorDto request,
        CancellationToken ct) => Reply(await service.CreateAsync(request, ct));

    [HttpPost("{id:long}/{action}")]
    public async Task<IActionResult> Transition(long id, string action,
        [FromBody] FacilityVisitorTransitionDto request, CancellationToken ct) =>
        Reply(await service.TransitionAsync(id, action, request, ct));


    [HttpPost("export"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Export([FromBody] FacilityVisitorExportRequestDto request,
        CancellationToken ct = default)
    {
        var snapshot = await service.ExportSnapshotAsync(request.CampusId, request.FromUtc, request.ToUtc, ct);
        if (!snapshot.Success || snapshot.Data == null) return Reply(snapshot);
        var bn = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bn";
        static string Csv(object? value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (text.TrimStart().Length > 0 && "=+-@".Contains(text.TrimStart()[0]))
                text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder(bn
            ? "ভিজিট,ক্যাম্পাস,অবস্থা,নির্ধারিত শুরু UTC,নির্ধারিত শেষ UTC,প্রবেশ UTC,প্রস্থান UTC,ঘটনার সংখ্যা\r\n"
            : "VisitId,CampusId,State,VisitStartUtc,VisitEndUtc,CheckedInUtc,CheckedOutUtc,IncidentCount\r\n");
        foreach (var row in snapshot.Data)
            csv.AppendJoin(',', Csv(row.VisitId), Csv(row.CampusId), Csv(row.StateCode),
                Csv(row.VisitStartUtc.ToString("O", CultureInfo.InvariantCulture)),
                Csv(row.VisitEndUtc.ToString("O", CultureInfo.InvariantCulture)),
                Csv(row.CheckedInAtUtc?.ToString("O", CultureInfo.InvariantCulture)),
                Csv(row.CheckedOutAtUtc?.ToString("O", CultureInfo.InvariantCulture)),
                Csv(row.IncidentCount)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "facility-visitor-access-summary.csv");
    }

    [HttpGet("{id:long}/history")]
    public async Task<IActionResult> History(long id, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.HistoryAsync(id, page, pageSize, ct));
}
