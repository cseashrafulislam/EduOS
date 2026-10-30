using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/transport/family-attendance")]
[Authorize(Roles = "Student,Guardian,Parent")]
[RequireModule("TRANSPORT")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportFamilyAttendanceController : ControllerBase
{
    private readonly ITransportTripService _service;
    public TransportFamilyAttendanceController(ITransportTripService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly date,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetFamilyAttendanceAsync(date, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/history")]
    public async Task<IActionResult> History(Guid reference, [FromQuery] string studentCode,
        CancellationToken ct = default)
    {
        var result = await _service.GetFamilyAttendanceHistoryAsync(reference, studentCode, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly date, CancellationToken ct = default)
    {
        var result = await _service.ExportFamilyAttendanceCsvAsync(date, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8",
            "my-transport-attendance.csv");
    }
}
