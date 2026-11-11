using EduOS.Core.DTOs.SaaS;
using EduOS.Service.Services.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/smart-setup")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SmartSetupApiController(SmartSetupService service) : ControllerBase
{
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(CancellationToken ct)
    {
        var result = await service.PreviewAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] ApplySmartSetupRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.ApplyAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
