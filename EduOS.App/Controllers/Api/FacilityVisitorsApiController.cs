using System.Globalization;
using System.Security.Claims;
using EduOS.App.Security;
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
    // Attach the exact public cookie-session reference and institution to private responses.
    private bool BindCurrentSession()
    {
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        var tenant = User.FindFirstValue("TenantId");
        if (sid is not { Length: 48 } || !long.TryParse(tenant, out var tenantId) || tenantId <= 0)
            return false;
        Response.Headers["X-EduOS-Session-Reference"] = AuthSessionRegistry.ToPublicReference(sid);
        Response.Headers["X-EduOS-Tenant-Id"] = tenant;
        return true;
    }

    private IActionResult SessionUnavailable() =>
        Unauthorized(new { success = false, message = "Session unavailable; sign in again." });

    private IActionResult Reply<T>(ApiResponse<T> response) =>
        BindCurrentSession() ? StatusCode(response.StatusCode, response) : SessionUnavailable();

    [HttpGet]
    public async Task<IActionResult> List(long? campusId, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.ListAsync(campusId, page, pageSize, ct));

    [HttpGet("hosts"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Hosts(long campusId, string? search = null, int page = 1,
        int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.HostsAsync(campusId, search, page, pageSize, ct));

    [HttpGet("occupancy"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Occupancy(long campusId, CancellationToken ct = default) =>
        Reply(await service.OccupancyAsync(campusId, ct));

    [HttpGet("privacy-audit"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> PrivacyAudit(CancellationToken ct = default) =>
        Reply(await service.PrivacyAuditReportAsync(ct));

    [HttpGet("policies"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Policies(long campusId, int page = 1, int pageSize = 25,
        CancellationToken ct = default) =>
        Reply(await service.PolicyChangesAsync(campusId, page, pageSize, ct));

    [HttpPost("policies"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> RequestPolicy([FromBody] FacilityVisitorPolicyRequestDto request,
        CancellationToken ct) => Reply(await service.RequestPolicyChangeAsync(request, ct));

    [HttpPost("policies/{id:long}/{decision}"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> DecidePolicy(long id, string decision,
        [FromBody] FacilityVisitorPolicyDecisionDto request, CancellationToken ct) =>
        Reply(await service.DecidePolicyChangeAsync(id, decision, request, ct));

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
        if (!BindCurrentSession()) return SessionUnavailable();
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
