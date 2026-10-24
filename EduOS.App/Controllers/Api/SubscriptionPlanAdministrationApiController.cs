using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/platform/subscription-plans")]
[Authorize(Roles = "SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SubscriptionPlanAdministrationApiController(ISubscriptionPlanService plans) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await plans.GetAdminPlanAvailabilityAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{id:long}/configuration")]
    public async Task<IActionResult> Configuration(long id, CancellationToken ct)
    {
        var result = await plans.GetPlanConfigurationAsync(id, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id:long}/configuration")]
    public async Task<IActionResult> SaveConfiguration(long id,
        [FromBody] SaveSubscriptionPlanConfigurationRequestDto request, CancellationToken ct)
    {
        var result = await plans.SavePlanConfigurationAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id:long}/availability")]
    public async Task<IActionResult> ChangeAvailability(long id,
        [FromBody] ChangeSubscriptionPlanAvailabilityRequestDto request, CancellationToken ct)
    {
        var result = await plans.ChangePlanAvailabilityAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
