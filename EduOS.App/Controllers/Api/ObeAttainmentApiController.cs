using System.Globalization;
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

[ApiController, Route("api/accreditation/obe"), Authorize, RequireModule("ACCREDITATION"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ObeAttainmentApiController(IObeAttainmentService service) : ControllerBase
{
    private bool Session()
    {
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        var tenant = User.FindFirstValue("TenantId");
        if (sid is not { Length: 48 } || !long.TryParse(tenant, out var id) || id <= 0)
            return false;
        Response.Headers["X-EduOS-Session-Reference"] = AuthSessionRegistry.ToPublicReference(sid);
        Response.Headers["X-EduOS-Tenant-Id"] = tenant;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return true;
    }

    private IActionResult Reply<T>(ApiResponse<T> result) => Session()
        ? StatusCode(result.StatusCode, result)
        : Unauthorized(new { success = false, message = "Session unavailable." });

    [HttpGet("courses")]
    public async Task<IActionResult> Courses(long campusId, long programId, CancellationToken ct) =>
        Reply(await service.CoursesAsync(campusId, programId, ct));

    [HttpGet("publications")]
    public async Task<IActionResult> Publications(long campusId, long programId, CancellationToken ct) =>
        Reply(await service.PublicationsAsync(campusId, programId, ct));

    [HttpGet("publications/{id:long}/component-options")]
    public async Task<IActionResult> ComponentOptions(long id, long campusId, long programId, CancellationToken ct) =>
        Reply(await service.ComponentOptionsAsync(campusId, programId, id, ct));

    [HttpGet("policies")]
    public async Task<IActionResult> Policies(long campusId, long programId, CancellationToken ct) =>
        Reply(await service.PoliciesAsync(campusId, programId, ct));

    [HttpGet("policies/{id:long}")]
    public async Task<IActionResult> Policy(long id, CancellationToken ct) =>
        Reply(await service.PolicyAsync(id, ct));

    [HttpPost("policies")]
    public async Task<IActionResult> CreatePolicy([FromBody] ObePolicyCreateDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.CreatePolicyAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpGet("policies/{id:long}/preview")]
    public async Task<IActionResult> Preview(long id, long publicationId, CancellationToken ct) =>
        Reply(await service.PreviewAsync(id, publicationId, ct));

    [HttpPost("policies/{id:long}/reports")]
    public async Task<IActionResult> Submit(long id, [FromBody] ObeSubmitDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.SubmitAsync(id, input, ct)) : ValidationProblem(ModelState);

    [HttpGet("reports")]
    public async Task<IActionResult> Reports(long campusId, long programId, CancellationToken ct) =>
        Reply(await service.ReportsAsync(campusId, programId, ct));

    [HttpGet("reports/{reference:guid}")]
    public async Task<IActionResult> Report(Guid reference, CancellationToken ct) =>
        Reply(await service.ReportAsync(reference, ct));

    [HttpPost("reports/{reference:guid}/review")]
    public async Task<IActionResult> Review(Guid reference, [FromBody] ObeReviewDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.ReviewAsync(reference, input, ct)) : ValidationProblem(ModelState);

    // Approved, aggregate-only CSV. No learner names, marks or private evidence files.
    [HttpGet("reports/{reference:guid}/approved.csv")]
    public async Task<IActionResult> ApprovedCsv(Guid reference, CancellationToken ct)
    {
        if (!Session()) return Unauthorized();
        var response = await service.ReportAsync(reference, ct);
        if (!response.Success || response.Data == null) return Reply(response);
        var report = response.Data;
        if (report.StateCode != "Approved") return NotFound();
        var csv = new StringBuilder("ReportReference,PolicyId,PublicationId,PublicationVersion,Type,Outcome,EvidenceSource,AttainmentPercent,EvidenceCount\r\n");
        static string Safe(string value)
        {
            var clean = value.Replace('"', ' ').Replace('\r', ' ').Replace('\n', ' ');
            if (clean.Length > 0 && "=+-@".Contains(clean[0])) clean = "'" + clean;
            return "\"" + clean + "\"";
        }
        foreach (var clo in report.Snapshot.CourseOutcomes)
            csv.Append(report.Reference).Append(',').Append(report.PolicyId).Append(',')
                .Append(report.ResultPublicationId).Append(',').Append(report.PublicationVersionNo)
                .Append(",CLO,").Append(Safe(clo.CourseOutcomeCode)).Append(',')
                .Append(Safe(clo.ComponentCode ?? "CourseGradeProxy")).Append(',')
                .Append(clo.AttainmentPercent.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(report.Snapshot.LearnerCount).Append("\r\n");
        foreach (var plo in report.Snapshot.Outcomes)
            csv.Append(report.Reference).Append(',').Append(report.PolicyId).Append(',')
                .Append(report.ResultPublicationId).Append(',').Append(report.PublicationVersionNo)
                .Append(",PLO,").Append(Safe(plo.ProgramOutcomeCode)).Append(',')
                .Append(Safe("WeightedPublishedEvidence")).Append(',')
                .Append(plo.AttainmentPercent.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(plo.EvidenceCount).Append("\r\n");
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "obe-attainment-" + report.Reference.ToString("N") + ".csv");
    }
}
