using System.ComponentModel.DataAnnotations;
using EduOS.App.Authorization;
using EduOS.Service.Services.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

public sealed class AiInstitutionGeneralRequest
{
    [Required, StringLength(200, MinimumLength = 5)]
    public string Question { get; set; } = string.Empty;
    public bool ConsentToExternalAi { get; set; }
}

public sealed class AiInstitutionAskRequest
{
    [Required, StringLength(120, MinimumLength = 3)]
    public string Question { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
}

[ApiController]
[Route("api/ai/institution")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("REPORTING")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiInstitutionQueryController(AiInstitutionMetricService service,
    EduOsAiWorkspaceService provider) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(new
    {
        success = true,
        data = new
        {
            verifiedLocalAvailable = true,
            generalAiAvailable = await provider.IsCurrentRoleReadyAsync(ct)
        }
    });

    // Model-free MIS questions are not subject to billable AI rate limits.
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AiInstitutionAskRequest request, CancellationToken ct)
    {
        if (request is null || !ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.AskAsync(request.Question, request.FromDate, request.ToDate, ct);
        return StatusCode(result.StatusCode, result);
    }

    // Same Ask EduOS Q&A page; separate backend policy ensures only the
    // paid fallback is subject to strict per-user external-model rate limits.
    [HttpPost("general")]
    [EnableRateLimiting("AiGenerationPolicy")]
    public async Task<IActionResult> General(
        [FromBody] AiInstitutionGeneralRequest request, CancellationToken ct)
    {
        if (request is null || !ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await provider.AnswerGeneralKnowledgeAsync(
            request.Question, request.ConsentToExternalAi, ct);
        return StatusCode(result.StatusCode, result);
    }
}
