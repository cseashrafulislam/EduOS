using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

// Separate read-only operational endpoint; reuses the canonical TransportService.
[ApiController]
[Route("api/transport/driver-readiness")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportDriverReadinessController : ControllerBase
{
    private readonly ITransportService _service;
    public TransportDriverReadinessController(ITransportService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15,
        [FromQuery] string? search = null, [FromQuery] string? state = "Attention",
        [FromQuery] int withinDays = 30, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetDriverReadinessAsync(campusId, page, pageSize, search, state, withinDays, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] string? state = "Attention",
        [FromQuery] int withinDays = 30, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportDriverReadinessCsvAsync(campusId, search, state, withinDays, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "vehicle-driver-readiness.csv");
    }
}
