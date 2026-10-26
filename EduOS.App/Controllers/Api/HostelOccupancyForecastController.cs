using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Read-only future occupancy planning; never returns student or billing data.</summary>
[ApiController]
[Route("api/hostel/occupancy-forecast")]
[Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
[RequireModule("HOSTEL")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HostelOccupancyForecastController : ControllerBase
{
    private readonly IHostelService _service;
    public HostelOccupancyForecastController(IHostelService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly operatingDate,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null,
        [FromQuery] bool attentionOnly = true, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetOccupancyForecastAsync(
            operatingDate, campusId, page, pageSize, search, attentionOnly, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly operatingDate,
        [FromQuery] long? campusId = null, [FromQuery] string? search = null,
        [FromQuery] bool attentionOnly = true, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportOccupancyForecastCsvAsync(
            operatingDate, campusId, search, attentionOnly, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-future-occupancy.csv");
    }
}
