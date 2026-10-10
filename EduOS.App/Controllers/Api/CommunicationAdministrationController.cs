using EduOS.Core.DTOs.Communication;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/communication-admin")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CommunicationAdministrationController(ICommunicationAdministrationService service) : ControllerBase
{
    [HttpGet("gateways")]
    public async Task<IActionResult> Gateways(CancellationToken ct)
    {
        var result = await service.GetGatewaysAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("gateways")]
    public async Task<IActionResult> CreateGateway([FromBody] SaveCommunicationGatewayRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveGatewayAsync(null, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("gateways/{id:long}")]
    public async Task<IActionResult> UpdateGateway(long id, [FromBody] SaveCommunicationGatewayRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveGatewayAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("templates")]
    public async Task<IActionResult> Templates([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.GetTemplatesAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] SaveMessageTemplateRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveTemplateAsync(null, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("templates/{id:long}")]
    public async Task<IActionResult> UpdateTemplate(long id, [FromBody] SaveMessageTemplateRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveTemplateAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("notice-categories")]
    public async Task<IActionResult> Categories(CancellationToken ct)
    {
        var result = await service.GetNoticeCategoriesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("notice-categories")]
    public async Task<IActionResult> CreateCategory([FromBody] SaveNoticeCategoryRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveNoticeCategoryAsync(null, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("notice-categories/{id:long}")]
    public async Task<IActionResult> UpdateCategory(long id, [FromBody] SaveNoticeCategoryRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveNoticeCategoryAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
