using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/platform/catalog/modules")]
[Authorize(Roles = "SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlatformProductModuleAdministrationApiController(IPlatformCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await catalog.GetAdminProductModulesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id:long}/configuration")]
    public async Task<IActionResult> Save(long id,
        [FromBody] SavePlatformProductModuleRequestDto request, CancellationToken ct)
    {
        var result = await catalog.SaveProductModuleConfigurationAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
