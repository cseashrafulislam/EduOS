using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

/// <summary>Privacy-safe interval dispatch risk; read-only and tenant-admin scoped.</summary>
[ApiController]
[Route("api/transport/driver-coverage-window")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportDriverCoverageWindowController : ControllerBase
{
    private readonly ITransportService _service;
    public TransportDriverCoverageWindowController(ITransportService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 15,
        [FromQuery] string? search = null, [FromQuery] bool attentionOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetDriverCoverageWindowAsync(
            fromDate, toDate, campusId, page, pageSize, search, attentionOnly, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
        [FromQuery] long? campusId = null, [FromQuery] string? search = null,
        [FromQuery] bool attentionOnly = true, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportDriverCoverageWindowCsvAsync(
            fromDate, toDate, campusId, search, attentionOnly, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data),
            "text/csv; charset=utf-8", "driver-coverage-window.csv");
    }
}
