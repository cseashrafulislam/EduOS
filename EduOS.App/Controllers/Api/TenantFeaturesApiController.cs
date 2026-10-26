using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/tenant-features")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantFeaturesApiController(ITenantFeatureRestrictionService service) : ControllerBase
{

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetAsync(page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{featureId:long}")]
    public async Task<IActionResult> Save(long featureId,
        [FromBody] SaveTenantFeatureRestrictionRequestDto request, CancellationToken cancellationToken)
    {
        var result = await service.SaveAsync(featureId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
