using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/platform/catalog/module-features")]
[Authorize(Roles = "SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlatformModuleFeaturesApiController(IPlatformCatalogService catalog) : ControllerBase
{
    [HttpGet("{moduleId:long}")]
    public async Task<IActionResult> List(long moduleId, CancellationToken ct)
    {
        var result = await catalog.GetAdminModuleFeaturesAsync(moduleId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{moduleId:long}/features/{featureId:long}")]
    public async Task<IActionResult> Save(long moduleId, long featureId,
        [FromBody] SavePlatformModuleFeatureRequestDto request, CancellationToken ct)
    {
        var result = await catalog.SaveModuleFeatureMappingAsync(moduleId, featureId, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
