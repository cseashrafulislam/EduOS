using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/tenant-settings")]
public sealed class TenantSettingController : ControllerBase
{
    private readonly ITenantSettingService _service;
    public TenantSettingController(ITenantSettingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var response = await _service.GetSettingsAsync(category, page, pageSize, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("key/{key}")]
    public async Task<IActionResult> Get(string key, CancellationToken cancellationToken)
    {
        var response = await _service.GetSettingAsync(key, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Create([FromBody] SaveTenantSettingRequestDto request, CancellationToken cancellationToken)
    {
        var response = await _service.SaveSettingAsync(null, request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut("{settingId:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Update(long settingId, [FromBody] SaveTenantSettingRequestDto request, CancellationToken cancellationToken)
    {
        var response = await _service.SaveSettingAsync(settingId, request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("terminology")]
    public async Task<IActionResult> Terminology(CancellationToken cancellationToken)
    {
        var response = await _service.GetTerminologyAsync(cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("terminology")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CreateTerminology([FromBody] SaveTenantTerminologyRequestDto request, CancellationToken cancellationToken)
    {
        var response = await _service.SaveTerminologyAsync(null, request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut("terminology/{terminologyId:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateTerminology(long terminologyId, [FromBody] SaveTenantTerminologyRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _service.SaveTerminologyAsync(terminologyId, request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }
}
