using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/platform/tenants")]
[Authorize(Roles = "SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlatformTenantsApiController(IPlatformTenantLifecycleService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? search, [FromQuery] TenantState? state,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.SearchAsync(search, state, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{tenantId:long}/history")]
    public async Task<IActionResult> History(long tenantId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.GetStateHistoryAsync(tenantId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{tenantId:long}/state")]
    public async Task<IActionResult> ChangeState(long tenantId,
        [FromBody] ChangePlatformTenantStateRequestDto request, CancellationToken ct)
    {
        var result = await service.ChangeStateAsync(tenantId, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
