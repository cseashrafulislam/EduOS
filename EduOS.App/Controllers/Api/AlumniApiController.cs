using System.Text;
using EduOS.App.Authorization;
using EduOS.Core.DTOs.Alumni;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize]
[RequireModule("ALUMNI")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/alumni")]
public sealed class AlumniApiController : ControllerBase
{
    private readonly IAlumniService _service;
    public AlumniApiController(IAlumniService service) => _service = service;

    [HttpGet("me")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var result = await _service.MineAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("consent")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> SaveConsent([FromBody] AlumniConsentRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SaveConsentAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("directory")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Directory([FromQuery] AlumniDirectoryQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.DirectoryAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    // Export only the authorized page, never an unbounded tenant-wide dump.
    [HttpGet("directory.csv")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ExportPage([FromQuery] AlumniDirectoryQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.DirectoryAsync(query, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        var csv = new StringBuilder("\uFEFFStudent code,Name,Email,Phone,Completed (UTC),Campus ID\r\n");
        foreach (var item in result.Data.Items)
        {
            csv.Append(Csv(item.StudentCode)).Append(',')
                .Append(Csv(item.FullName)).Append(',')
                .Append(Csv(item.ContactEmail)).Append(',')
                .Append(Csv(item.ContactPhone)).Append(',')
                .Append(Csv(item.CompletedAtUtc.ToString("O"))).Append(',')
                .Append(item.CampusId).Append("\r\n");
        }
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"alumni-directory-page-{query.Page}.csv");
    }


    [HttpGet("event-campuses")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> EventCampuses(CancellationToken ct)
    {
        var result = await _service.EventCampusesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] AlumniEventQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.EventsAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("events")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> CreateEvent([FromBody] AlumniEventDraftDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CreateEventAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("events/{reference:guid}/publish")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> PublishEvent(Guid reference, [FromBody] AlumniEventActionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ChangeEventStatusAsync(reference, request, true, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("events/{reference:guid}/cancel")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> CancelEvent(Guid reference, [FromBody] AlumniEventActionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ChangeEventStatusAsync(reference, request, false, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("events/rsvp")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Rsvp([FromBody] AlumniEventRsvpDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.RsvpAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    // Consent-filtered, campus-scoped roster; no contact details.
    [HttpGet("events/{reference:guid}/attendance")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Attendance(Guid reference, [FromQuery] AlumniEventAttendanceQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.AttendanceAsync(reference, query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("events/{reference:guid}/attendance")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> CheckIn(Guid reference, [FromBody] AlumniEventCheckInDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CheckInAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    // Aggregated engagement only. Never export attendee identities or contact details.
    [HttpGet("events/report.csv")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> EventReport([FromQuery] AlumniEventQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.EventsAsync(query, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        var csv = new StringBuilder("\uFEFFEvent,Campus ID,Starts (UTC),Status,Going,Declined,Checked in\r\n");
        foreach (var item in result.Data.Items)
        {
            csv.Append(Csv(item.Title)).Append(',').Append(item.CampusId).Append(',')
                .Append(Csv(item.StartsAtUtc.ToString("O"))).Append(',')
                .Append(item.Status).Append(',').Append(item.GoingCount).Append(',')
                .Append(item.DeclinedCount).Append(',').Append(item.CheckedInCount).Append("\r\n");
        }
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"alumni-engagement-page-{query.Page}.csv");
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
