using EduOS.App.Authorization;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/v3/enterprise/ledger-trace"), Authorize(Roles = "TenantAdmin"),
 RequireModule("INVENTORY"), RequireModule("FINANCE"), EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class V3EnterpriseLedgerTraceController(EduOSDbContext db, ICurrentUserService user) : ControllerBase
{
    private readonly V3EnterpriseLedgerTraceService service = new(db, user);

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct)
    {
        var result = await service.GetAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/csv")]
    public async Task<IActionResult> Csv(Guid reference, CancellationToken ct)
    {
        var result = await service.GetAsync(reference, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        var d = result.Data;
        static string Cell(object? value)
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (s.Length > 0 && ("=+-@\t\r".Contains(s[0]))) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Evidence,Reference,Number,State,Debit,Credit,RecordedValue,SourceType,SourceId\r\n");
        csv.AppendJoin(",", new[] { Cell("Stock"), Cell(d.MovementReference), Cell(d.MovementNumber),
            Cell(d.State), Cell(""), Cell(""), Cell(d.RecordedMovementValue),
            Cell(d.SourceType), Cell(d.SourceId) }).Append("\r\n");
        foreach (var j in d.Journals)
            csv.AppendJoin(",", new[] { Cell("Journal"), Cell(j.Reference), Cell(j.Number),
                Cell(j.State), Cell(j.Debit), Cell(j.Credit), Cell(""), Cell(""), Cell("") }).Append("\r\n");
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "v3-ledger-trace-" + reference.ToString("N") + ".csv");
    }
    [HttpGet("asset-depreciation/{reference:guid}")]
    public async Task<IActionResult> AssetDepreciation(Guid reference, CancellationToken ct)
    {
        var result = await new V3EnterpriseAssetFinanceTraceService(db, user).GetAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("asset-depreciation/{reference:guid}/csv")]
    public async Task<IActionResult> AssetDepreciationCsv(Guid reference, CancellationToken ct)
    {
        var result = await new V3EnterpriseAssetFinanceTraceService(db, user).GetAsync(reference, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        var d = result.Data;
        static string Cell(object? value)
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Evidence,Reference,State,Debit,Credit,RunAmount,LinesAmount,Campus,PeriodEnd\r\n");
        csv.AppendJoin(",", new[] { Cell("AssetDepreciation"), Cell(d.RunReference),
            Cell(d.State), Cell(""), Cell(""), Cell(d.RunTotalAmount),
            Cell(d.DepreciationLinesAmount), Cell(d.CampusId), Cell(d.PeriodEnd) }).Append("\r\n");
        foreach (var j in d.Journals)
            csv.AppendJoin(",", new[] { Cell("Journal"), Cell(j.Reference), Cell(j.State),
                Cell(j.Debit), Cell(j.Credit), Cell(""), Cell(""), Cell(j.CampusId),
                Cell("") }).Append("\r\n");
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "v3-asset-finance-trace-" + reference.ToString("N") + ".csv");
    }

    // Only the requested page is exported. Detail is the authority for complete
    // journal balancing, reversal and historical tombstone reconciliation.
    [HttpGet("register")]
    public async Task<IActionResult> Register(
        [FromQuery] string kind, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var response = await new V3EnterpriseReconciliationRegisterService(db, user)
            .GetAsync(kind, from, to, campusId, page, pageSize, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("register/csv")]
    public async Task<IActionResult> RegisterCsv(
        [FromQuery] string kind, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var response = await new V3EnterpriseReconciliationRegisterService(db, user)
            .GetAsync(kind, from, to, campusId, page, pageSize, ct);
        if (!response.Success || response.Data == null)
            return StatusCode(response.StatusCode, response);
        static string Cell(object? value)
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Kind,Reference,Number,Date,Campus,State,RecordedValue,Lines,EvidenceStatus\r\n");
        foreach (var row in response.Data.Items)
            csv.AppendJoin(",", new[] { Cell(response.Data.Kind), Cell(row.Reference), Cell(row.Number),
                Cell(row.Date), Cell(row.CampusId), Cell(row.State), Cell(row.RecordedValue),
                Cell(row.LineCount), Cell(row.EvidenceStatus) }).Append("\r\n");
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "v3-reconciliation-register-" + response.Data.Kind + "-page-" +
            response.Data.Page.ToString(CultureInfo.InvariantCulture) + ".csv");
    }

}
