using System.ComponentModel.DataAnnotations;
using EduOS.App.Authorization;
using EduOS.Service.Services.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

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
public sealed class AiInstitutionQueryController(AiInstitutionMetricService service) : ControllerBase
{
    [HttpPost("ask")]
    [EnableRateLimiting("AiGenerationPolicy")]
    public async Task<IActionResult> Ask([FromBody] AiInstitutionAskRequest request, CancellationToken ct)
    {
        if (request is null || !ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.AskAsync(request.Question, request.FromDate, request.ToDate, ct);
        return StatusCode(result.StatusCode, result);
    }
}
