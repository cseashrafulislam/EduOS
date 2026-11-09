using EduOS.App.Authorization;
using EduOS.Core.DTOs.Library;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/ai/study")]
[Authorize(Roles = "TenantAdmin,Principal,Student,Teacher")]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GroundedStudyAssistantController(GroundedStudyAssistantService study) : ControllerBase
{
    [HttpGet("sources")]
    public async Task<IActionResult> Sources([FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await study.ListSourcesAsync(search, page, pageSize, false, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] GroundedStudyQuestionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await study.AskAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("author-sources")]
    [Authorize(Roles = "TenantAdmin,Teacher")]
    public async Task<IActionResult> AuthorSources([FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await study.ListSourcesAsync(search, page, pageSize, true, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("reviews/{sourceReference:guid}")]
    [Authorize(Roles = "TenantAdmin,Teacher")]
    public async Task<IActionResult> Reviews(Guid sourceReference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await study.ReviewQueueAsync(sourceReference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("excerpts")]
    [Authorize(Roles = "TenantAdmin,Teacher")]
    public async Task<IActionResult> Submit([FromBody] CreateGroundedStudyExcerptDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await study.SubmitAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("excerpts/{reference:guid}/review")]
    [Authorize(Roles = "TenantAdmin,Teacher")]
    public async Task<IActionResult> Review(Guid reference,
        [FromBody] ReviewGroundedStudyExcerptDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await study.ReviewAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
