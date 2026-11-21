using EduOS.Service.Services.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/ai/provider-settings")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantAiProviderSettingsController(TenantAiProviderSettingsService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await service.GetAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Save(
        [FromBody] TenantAiProviderSettingsService.SaveRequest request, CancellationToken ct)
    {
        var result = await service.SaveAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}