using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/platform/catalog/institution-types")]
[Authorize(Roles = "SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlatformCatalogAdministrationApiController(IPlatformCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await catalog.GetAdminInstitutionTypesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id:long}/activation")]
    public async Task<IActionResult> ChangeActivation(long id,
        [FromBody] ChangeInstitutionTypeActivationRequestDto request, CancellationToken ct)
    {
        var result = await catalog.ChangeInstitutionTypeActivationAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
