using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/usage-history")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsageHistoryApiController : ControllerBase
{
    private readonly IUsageHistoryService _service;

    // DI factory resolves the existing scoped DbContext, current-user service and logger.
    public UsageHistoryApiController(IServiceProvider provider)
    {
        _service = ActivatorUtilities.CreateInstance<UsageHistoryService>(provider);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetHistoryAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("capture")]
    public async Task<IActionResult> Capture(CancellationToken ct)
    {
        var result = await _service.CaptureTodayAsync(ct);
        return StatusCode(result.StatusCode, result);
    }
}
