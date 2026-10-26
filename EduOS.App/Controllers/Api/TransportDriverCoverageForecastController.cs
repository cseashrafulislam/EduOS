using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

/// <summary>Read-only future dispatch planning; excludes student and driver identity.</summary>
[ApiController]
[Route("api/transport/driver-coverage-forecast")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportDriverCoverageForecastController : ControllerBase
{
    private readonly ITransportService _service;
    public TransportDriverCoverageForecastController(ITransportService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly operatingDate,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 15,
        [FromQuery] string? search = null, [FromQuery] string? state = "Attention",
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetDriverCoverageForecastAsync(
            operatingDate, campusId, page, pageSize, search, state, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly operatingDate,
        [FromQuery] long? campusId = null, [FromQuery] string? search = null,
        [FromQuery] string? state = "Attention", CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportDriverCoverageForecastCsvAsync(
            operatingDate, campusId, search, state, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data),
            "text/csv; charset=utf-8", "future-driver-coverage.csv");
    }
}
