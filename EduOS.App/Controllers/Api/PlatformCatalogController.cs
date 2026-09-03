using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

/// <summary>
/// Public, read-only metadata used by signup and onboarding clients.
/// </summary>
[ApiController]
[Route("api/platform-catalog")]
[AllowAnonymous]
public class PlatformCatalogController : ControllerBase
{
    private readonly IPlatformCatalogService _catalogService;

    public PlatformCatalogController(IPlatformCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet("institution-types")]
    public async Task<IActionResult> GetInstitutionTypes()
    {
        var result = await _catalogService.GetInstitutionTypesAsync();
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("institution-types/{code}")]
    public async Task<IActionResult> GetInstitutionType(string code)
    {
        var result = await _catalogService.GetInstitutionTypeByCodeAsync(code);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("modules")]
    public async Task<IActionResult> GetModules()
    {
        var result = await _catalogService.GetModulesAsync();
        return StatusCode(result.StatusCode, result);
    }
}
