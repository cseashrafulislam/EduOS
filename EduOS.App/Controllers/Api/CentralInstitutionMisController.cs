using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/platform/institution-mis"), Authorize(Roles = "TenantAdmin"),
 RequireModule("REPORTING"), EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CentralInstitutionMisController(ICentralInstitutionMisService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, CancellationToken ct)
    {
        var result = await service.GetAsync(fromDate, toDate, ct);
        return StatusCode(result.StatusCode, result);
    }
}
