using System.Security.Claims;
using System.Text;
using EduOS.App.Authorization;
using EduOS.App.Security;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/accreditation"), Authorize, RequireModule("ACCREDITATION"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccreditationApiController(IAccreditationService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> result)
    {
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        var tenant = User.FindFirstValue("TenantId");
        if (sid is not { Length: 48 } || !long.TryParse(tenant, out var id) || id <= 0)
            return Unauthorized(new { success = false, message = "Session unavailable." });
        Response.Headers["X-EduOS-Session-Reference"] = AuthSessionRegistry.ToPublicReference(sid);
        Response.Headers["X-EduOS-Tenant-Id"] = tenant;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("campuses")]
    public async Task<IActionResult> Campuses(CancellationToken ct) => Reply(await service.CampusesAsync(ct));

    [HttpGet("programs")]
    public async Task<IActionResult> Programs(long campusId, CancellationToken ct) => Reply(await service.ProgramsAsync(campusId, ct));

    [HttpGet("reviewers")]
    public async Task<IActionResult> Reviewers(long campusId, string? search, CancellationToken ct) =>
        Reply(await service.ReviewersAsync(campusId, search, ct));

    [HttpGet("frameworks")]
    public async Task<IActionResult> Frameworks(CancellationToken ct) => Reply(await service.FrameworksAsync(ct));

    [HttpPost("frameworks")]
    public async Task<IActionResult> CreateFramework([FromBody] AccreditationFrameworkCreateDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.CreateFrameworkAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpPost("frameworks/{id:long}/publish")]
    public async Task<IActionResult> Publish(long id, [FromBody] AccreditationFrameworkPublishDto input,
        CancellationToken ct) => ModelState.IsValid
        ? Reply(await service.PublishFrameworkAsync(id, input, ct)) : ValidationProblem(ModelState);

    [HttpGet("frameworks/{id:long}/standards")]
    public async Task<IActionResult> Standards(long id, CancellationToken ct) => Reply(await service.StandardsAsync(id, ct));

    [HttpPost("standards")]
    public async Task<IActionResult> AddStandard([FromBody] AccreditationStandardCreateDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.AddStandardAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpGet("assessments")]
    public async Task<IActionResult> List(long? campusId, int page = 1, int pageSize = 20,
        CancellationToken ct = default) => Reply(await service.ListAsync(campusId, page, pageSize, ct));

    [HttpPost("assessments")]
    public async Task<IActionResult> Create([FromBody] AccreditationAssessmentCreateDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.CreateAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpGet("assessments/{reference:guid}")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct) => Reply(await service.DetailAsync(reference, ct));

    [HttpGet("assessments/{reference:guid}/history")]
    public async Task<IActionResult> History(Guid reference, int page = 1, int pageSize = 20,
        CancellationToken ct = default) => Reply(await service.HistoryAsync(reference, page, pageSize, ct));

    [HttpPost("assessments/{reference:guid}/{action}")]
    public async Task<IActionResult> Command(Guid reference, string action,
        [FromBody] AccreditationAssessmentCommandDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.CommandAsync(reference, action, input, ct)) : ValidationProblem(ModelState);

    [HttpGet("reports")]
    public async Task<IActionResult> Report(long campusId, DateTime from, DateTime to, CancellationToken ct) =>
        Reply(await service.ReportAsync(campusId, from, to, ct));

    // Only aggregate, scope-filtered fields can be exported; no evidence text or reviewer reasons.
    [HttpGet("reports.csv")]
    public async Task<IActionResult> Csv(long campusId, DateTime from, DateTime to, CancellationToken ct)
    {
        var result = await service.ReportAsync(campusId, from, to, ct);
        if (!result.Success || result.Data == null) return Reply(result);
        static string Escape(string input)
        {
            if (input.Length > 0 && "=+-@\t\r\n".Contains(input[0])) input = "'" + input;
            return "\"" + input.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Campus,Program,Framework,Version,Standard,Total,Approved,Pending,Expired\r\n");
        foreach (var row in result.Data)
            csv.Append(row.CampusId).Append(',').Append(row.AcademicProgramId?.ToString() ?? "")
                .Append(',').Append(Escape(row.FrameworkCode)).Append(',').Append(row.FrameworkVersion)
                .Append(',').Append(Escape(row.StandardCode)).Append(',').Append(row.Total)
                .Append(',').Append(row.Approved).Append(',').Append(row.Pending).Append(',')
                .Append(row.Expired).Append("\r\n");
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", "accreditation-summary.csv");
    }
}
