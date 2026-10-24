using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/platform/catalog/features")]
[Authorize(Roles = "SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlatformFeatureAdministrationApiController(IPlatformCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await catalog.GetAdminFeaturesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id:long}/configuration")]
    public async Task<IActionResult> Save(long id,
        [FromBody] SavePlatformFeatureRequestDto request, CancellationToken ct)
    {
        var result = await catalog.SaveFeatureConfigurationAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
