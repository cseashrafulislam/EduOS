using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/tenant-domains")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantDomainsApiController(ITenantDomainService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await service.GetDomainsAsync(ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterTenantDomainRequestDto request, CancellationToken ct)
    {
        var result = await service.RegisterDomainAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{id:long}/verify")]
    public async Task<IActionResult> Verify(long id, [FromBody] ChangeTenantDomainRequestDto request, CancellationToken ct)
    {
        var result = await service.VerifyDomainAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{id:long}/primary")]
    public async Task<IActionResult> Primary(long id, [FromBody] ChangeTenantDomainRequestDto request, CancellationToken ct)
    {
        var result = await service.SetPrimaryAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{id:long}/deactivate")]
    public async Task<IActionResult> Deactivate(long id, [FromBody] ChangeTenantDomainRequestDto request, CancellationToken ct)
    {
        var result = await service.DeactivateAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
