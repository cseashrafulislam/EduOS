using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/platform/catalog/institution-module-presets")]
[Authorize(Roles = "SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlatformInstitutionModulePresetsApiController(IPlatformCatalogService catalog) : ControllerBase
{
    [HttpGet("{institutionTypeId:long}")]
    public async Task<IActionResult> List(long institutionTypeId, CancellationToken ct)
    {
        var result = await catalog.GetAdminInstitutionModulePresetAsync(institutionTypeId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{institutionTypeId:long}/modules/{moduleId:long}")]
    public async Task<IActionResult> Save(long institutionTypeId, long moduleId,
        [FromBody] SavePlatformInstitutionModulePresetDto request, CancellationToken ct)
    {
        var result = await catalog.SaveInstitutionModulePresetAsync(institutionTypeId, moduleId, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
