using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Read-only interval peak occupancy planning, scoped to permitted campuses.</summary>
[ApiController]
[Route("api/hostel/peak-occupancy-forecast")]
[Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
[RequireModule("HOSTEL")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HostelPeakOccupancyForecastController : ControllerBase
{
    private readonly IHostelService _service;
    public HostelPeakOccupancyForecastController(IHostelService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, [FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] bool attentionOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetPeakOccupancyForecastAsync(
            fromDate, toDate, campusId, page, pageSize, search, attentionOnly, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, [FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] bool attentionOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportPeakOccupancyForecastCsvAsync(
            fromDate, toDate, campusId, search, attentionOnly, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-peak-occupancy.csv");
    }
}
