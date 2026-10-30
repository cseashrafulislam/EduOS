using EduOS.App.Authorization;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/transport/driver-trips")]
[Authorize(Roles = "Staff")]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportDriverTripsController : ControllerBase
{
    private readonly ITransportTripService _service;
    public TransportDriverTripsController(ITransportTripService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetDriverTripsAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/riders")]
    public async Task<IActionResult> Riders(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await _service.GetDriverRidersAsync(reference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/export")]
    public async Task<IActionResult> Export(Guid reference, CancellationToken ct = default)
    {
        var result = await _service.ExportDriverRidersCsvAsync(reference, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "my-transport-trip-riders.csv");
    }

    [HttpGet("{reference:guid}/no-show/preview")]
    public async Task<IActionResult> NoShowPreview(Guid reference, CancellationToken ct = default)
    {
        var result = await _service.GetDriverNoShowPreviewAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/no-show")]
    public async Task<IActionResult> MarkAwaitingNoShow(Guid reference,
        [FromBody] MarkTransportTripAwaitingNoShowRequestDto request, CancellationToken ct)
    {
        var result = await _service.MarkDriverAwaitingNoShowAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/attendance")]
    public async Task<IActionResult> Attendance(Guid reference,
        [FromBody] RecordTransportTripAttendanceRequestDto request, CancellationToken ct)
    {
        var result = await _service.RecordDriverAttendanceAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
