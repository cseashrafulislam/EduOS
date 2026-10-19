using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

/// <summary>Read-only TenantAdmin delivery history; message bodies are not exposed.</summary>
[ApiController]
[Route("api/communication-admin/deliveries")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CommunicationDeliveryHistoryApiController(ICommunicationAdministrationService service) : ControllerBase
{
    [HttpGet("export")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export([FromQuery] int? channel = null,
        [FromQuery] int? state = null, CancellationToken ct = default)
    {
        var result = await service.ExportDeliveriesCsvAsync(channel, state, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8",
            $"communication-deliveries-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] int? channel = null,
        [FromQuery] int? state = null, CancellationToken ct = default)
    {
        var result = await service.GetDeliveriesAsync(page, pageSize, channel, state, ct);
        return StatusCode(result.StatusCode, result);
    }
}
