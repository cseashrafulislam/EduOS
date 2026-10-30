using EduOS.App.Authorization;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
namespace EduOS.App.Controllers.Api;
[ApiController]
[Route("api/transport/trips")]
[Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportTripsController : ControllerBase
{
    private readonly ITransportTripService _service;
    public TransportTripsController(ITransportTripService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly date,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await _service.GetAsync(date, campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Plan([FromBody] PlanTransportTripRequestDto request, CancellationToken ct)
    {
        var result = await _service.PlanAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("schedule/calendar")]
    public async Task<IActionResult> ScheduleCalendar([FromQuery] Guid routeReference,
        [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate,
        CancellationToken ct = default)
    {
        var result = await _service.GetScheduleCalendarAsync(routeReference, startDate, endDate, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> Schedule([FromBody] ScheduleTransportTripsRequestDto request, CancellationToken ct)
    {
        var result = await _service.ScheduleAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/dispatch")]
    public async Task<IActionResult> Dispatch(Guid reference,
        [FromBody] DispatchTransportTripRequestDto request, CancellationToken ct)
    {
        var result = await _service.DispatchAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/complete")]
    public async Task<IActionResult> Complete(Guid reference,
        [FromBody] CompleteTransportTripRequestDto request, CancellationToken ct)
    {
        var result = await _service.CompleteAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/no-show/preview")]
    public async Task<IActionResult> NoShowPreview(Guid reference, CancellationToken ct = default)
    {
        var result = await _service.GetNoShowPreviewAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/no-show")]
    public async Task<IActionResult> MarkAwaitingNoShow(Guid reference,
        [FromBody] MarkTransportTripAwaitingNoShowRequestDto request, CancellationToken ct)
    {
        var result = await _service.MarkAwaitingNoShowAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/attendance")]
    public async Task<IActionResult> Attendance(Guid reference,
        [FromBody] RecordTransportTripAttendanceRequestDto request, CancellationToken ct)
    {
        var result = await _service.RecordAttendanceAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/attendance/corrections")]
    public async Task<IActionResult> CorrectAttendance(Guid reference,
        [FromBody] CorrectTransportTripAttendanceRequestDto request, CancellationToken ct)
    {
        var result = await _service.CorrectAttendanceAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/attendance/history")]
    public async Task<IActionResult> AttendanceHistory(Guid reference,
        [FromQuery] string studentCode, CancellationToken ct = default)
    {
        var result = await _service.GetAttendanceHistoryAsync(reference, studentCode, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/riders")]
    public async Task<IActionResult> Riders(Guid reference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await _service.GetRidersAsync(reference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/audit")]
    [Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Audit(Guid reference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _service.GetAttendanceAuditAsync(reference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/audit/export")]
    [Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ExportAudit(Guid reference, CancellationToken ct = default)
    {
        var result = await _service.ExportAttendanceAuditCsvAsync(reference, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8",
            "transport-trip-attendance-audit.csv");
    }

    [HttpGet("{reference:guid}/export")]
    public async Task<IActionResult> Export(Guid reference, CancellationToken ct = default)
    {
        var result = await _service.ExportRidersCsvAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "transport-trip-riders.csv");
    }
}
