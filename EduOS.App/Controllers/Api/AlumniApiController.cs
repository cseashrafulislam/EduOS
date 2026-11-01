using System.Globalization;
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
        var bengali = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bn";
        var csv = new StringBuilder(DirectoryHeader(bengali));
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
        var bengali = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bn";
        var csv = new StringBuilder(EngagementHeader(bengali));
        foreach (var item in result.Data.Items)
        {
            csv.Append(Csv(item.Title)).Append(',').Append(item.CampusId).Append(',')
                .Append(Csv(item.StartsAtUtc.ToString("O"))).Append(',')
                .Append(Csv(EventStatusLabel(item.Status, bengali))).Append(',').Append(item.GoingCount).Append(',')
                .Append(item.DeclinedCount).Append(',').Append(item.CheckedInCount).Append("\r\n");
        }
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"alumni-engagement-page-{query.Page}.csv");
    }

    // Export headings follow the already resolved tenant/user UI culture. ISO-8601 UTC values
    // and API status codes stay canonical; only presentation labels are localized.
    // Career employer onboarding is campus-scoped and visible only to TenantAdmin.
    [HttpGet("career/employers")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Employers([FromQuery] CareerEmployerQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.EmployersAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("career/employers")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> CreateEmployer([FromBody] CareerEmployerDraftDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CreateEmployerAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("career/employers/{reference:guid}/verify")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> VerifyEmployer(Guid reference, [FromBody] CareerEmployerVerifyDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.VerifyEmployerAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    // No employer contact or student application data; only current verified page.
    [HttpGet("career/employers.csv")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> EmployerReport([FromQuery] CareerEmployerQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        query.Status = 2;
        var result = await _service.EmployersAsync(query, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        var bn = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bn";
        var csv = new StringBuilder(bn
            ? "\uFEFFনিয়োগদাতা,ক্যাম্পাস আইডি,ওয়েবসাইট,যাচাইকৃত অবস্থা\r\n"
            : "\uFEFFEmployer,Campus ID,Website,Verification status\r\n");
        foreach (var item in result.Data.Items)
            csv.Append(Csv(item.Name)).Append(',').Append(item.CampusId).Append(',')
                .Append(Csv(item.Website)).Append(',').Append(Csv(bn ? "যাচাইকৃত" : "Verified"))
                .Append("\r\n");
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"career-verified-employers-page-{query.Page}.csv");
    }

    private static string DirectoryHeader(bool bengali) => bengali
        ? "\uFEFFশিক্ষার্থী কোড,নাম,ইমেইল,ফোন,সম্পন্ন (UTC),ক্যাম্পাস আইডি\r\n"
        : "\uFEFFStudent code,Name,Email,Phone,Completed (UTC),Campus ID\r\n";

    private static string EngagementHeader(bool bengali) => bengali
        ? "\uFEFFঅনুষ্ঠান,ক্যাম্পাস আইডি,শুরু (UTC),অবস্থা,আসবেন,আসবেন না,উপস্থিত\r\n"
        : "\uFEFFEvent,Campus ID,Starts (UTC),Status,Going,Declined,Checked in\r\n";

    private static string EventStatusLabel(int status, bool bengali) => (status, bengali) switch
    {
        (1, true) => "খসড়া",
        (2, true) => "প্রকাশিত",
        (3, true) => "বাতিল",
        (1, false) => "Draft",
        (2, false) => "Published",
        (3, false) => "Cancelled",
        _ => bengali ? "অজানা" : "Unknown"
    };

    private static string Csv(string? value)
    {
        var text = value ?? "";
        // Spreadsheet importers may ignore leading whitespace/BOM before evaluating a formula.
        // Preserve the original cell value, but force text mode before CSV quoting.
        var firstVisible = 0;
        while (firstVisible < text.Length &&
               (char.IsWhiteSpace(text[firstVisible]) || text[firstVisible] is '\uFEFF' or '\u200B'))
            firstVisible++;
        if (firstVisible < text.Length && (text[firstVisible] is '=' or '+' or '-' or '@'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
