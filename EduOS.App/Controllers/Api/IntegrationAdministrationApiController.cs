using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/integration-admin")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IntegrationAdministrationApiController(IIntegrationAdministrationService service) : ControllerBase
{
    [HttpGet("webhooks")]
    public async Task<IActionResult> Webhooks([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    { var r = await service.GetWebhookEndpointsAsync(page, pageSize, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("webhooks")]
    public async Task<IActionResult> CreateWebhook([FromBody] SaveWebhookEndpointRequestDto request, CancellationToken ct)
    { var r = await service.SaveWebhookEndpointAsync(null, request, ct); return StatusCode(r.StatusCode, r); }

    [HttpPut("webhooks/{id:long}")]
    public async Task<IActionResult> UpdateWebhook(long id, [FromBody] SaveWebhookEndpointRequestDto request, CancellationToken ct)
    { var r = await service.SaveWebhookEndpointAsync(id, request, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("webhooks/{id:long}/deliveries")]
    public async Task<IActionResult> Deliveries(long id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    { var r = await service.GetWebhookDeliveriesAsync(id, page, pageSize, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("credentials")]
    public async Task<IActionResult> Credentials([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    { var r = await service.GetApiCredentialsAsync(page, pageSize, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("credentials")]
    public async Task<IActionResult> CreateCredential([FromBody] CreateApiCredentialRequestDto request, CancellationToken ct)
    { var r = await service.CreateApiCredentialAsync(request, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("credentials/{id:long}/revoke")]
    public async Task<IActionResult> RevokeCredential(long id, [FromBody] RevokeApiCredentialRequestDto request, CancellationToken ct)
    { var r = await service.RevokeApiCredentialAsync(id, request, ct); return StatusCode(r.StatusCode, r); }
}
