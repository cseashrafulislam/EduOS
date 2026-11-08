using EduOS.App.Authorization;
using EduOS.Core.DTOs.Finance;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-scholarships")]
[Authorize]
[RequireModule("STUDENT")]
[RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ScholarshipApplicationsController(ScholarshipWorkflowService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(int page = 1, int pageSize = 30,
        string? state = null, CancellationToken ct = default)
    {
        var result = await service.SearchAsync(page, pageSize, state, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct)
    {
        var result = await service.GetAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitScholarshipRequest request, CancellationToken ct)
    {
        var result = await service.SubmitAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/decision")]
    public async Task<IActionResult> Decide(Guid reference,
        [FromBody] ScholarshipDecisionRequest request, CancellationToken ct)
    {
        var result = await service.DecideAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/award")]
    public async Task<IActionResult> Award(Guid reference, CancellationToken ct)
    {
        var result = await service.AwardAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid reference,
        [FromBody] ScholarshipCommandRequest request, CancellationToken ct)
    {
        var result = await service.CancelAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/evidence-download")]
    public async Task<IActionResult> DownloadEvidence(Guid reference, CancellationToken ct)
    {
        var result = await service.DownloadEvidenceAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation(int page = 1, int pageSize = 30,
        CancellationToken ct = default)
    {
        var result = await service.ReconciliationAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
}
