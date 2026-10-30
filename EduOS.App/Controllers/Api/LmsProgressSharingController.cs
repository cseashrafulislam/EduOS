using System.Globalization;
using System.Text;
using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "Student,Guardian,Parent")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/lms/progress-sharing")]
public sealed class LmsProgressSharingController(ILmsProgressGuardianService service) : ControllerBase
{
    [HttpGet("{enrollmentId:long}/guardians")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Guardians(long enrollmentId, CancellationToken ct)
    {
        var result = await service.EligibleAsync(enrollmentId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{enrollmentId:long}/grants")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Grants(long enrollmentId, CancellationToken ct)
    {
        var result = await service.GrantsAsync(enrollmentId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("grants")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Grant([FromBody] GrantLmsProgressGuardianRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GrantAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("grants/{reference:guid}/revoke")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Revoke(Guid reference,
        [FromBody] RevokeLmsProgressGuardianRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.RevokeAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Guardian,Parent")]
    public async Task<IActionResult> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.SharedAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    // Fresh authorization on every download, including after consent withdrawal.
    [HttpGet("mine/export")]
    [Authorize(Roles = "Guardian,Parent")]
    public async Task<IActionResult> Export([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.SharedAsync(page, pageSize, ct);
        if (!result.Success || result.Data is null)
            return StatusCode(result.StatusCode, result);
        var csv = new StringBuilder("Learner,Course,Completed lessons,Published lessons,Completion (%)\r\n");
        foreach (var row in result.Data.Items)
            csv.Append(CsvCell(row.LearnerName)).Append(',')
                .Append(CsvCell(row.CourseTitle)).Append(',')
                .Append(row.CompletedLessons.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PublishedLessons.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.LessonCompletionPercent.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("\r\n");
        Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes("\uFEFF" + csv),
            "text/csv; charset=utf-8", "lms-guardian-progress-visible.csv");
    }

    private static string CsvCell(string? input)
    {
        var value = input ?? string.Empty;
        var trimmed = value.TrimStart();
        if ((value.Length > 0 && (value[0] is '\t' or '\r' or '\n')) ||
            (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // Lesson CSV is reauthorized against the current consent and relationship on
    // each request; no client-supplied learner identity or cached rows are trusted.
    [HttpGet("mine/{enrollmentId:long}/lessons/export")]
    [Authorize(Roles = "Guardian,Parent")]
    public async Task<IActionResult> ExportLessons(long enrollmentId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.LessonsAsync(enrollmentId, page, pageSize, ct);
        if (!result.Success || result.Data is null)
            return StatusCode(result.StatusCode, result);
        var csv = new StringBuilder("Lesson,Completion (%),Completed,Completed at (UTC)\r\n");
        foreach (var row in result.Data.Items)
            csv.Append(CsvCell(row.LessonTitle)).Append(',')
                .Append(row.ProgressPercent.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.IsCompleted ? "Yes" : "No").Append(',')
                .Append(row.CompletedAt.HasValue
                    ? row.CompletedAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                    : string.Empty)
                .Append("\r\n");
        Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes("\uFEFF" + csv),
            "text/csv; charset=utf-8", "lms-guardian-lessons-visible.csv");
    }

    [HttpGet("mine/{enrollmentId:long}/lessons")]
    [Authorize(Roles = "Guardian,Parent")]
    public async Task<IActionResult> Lessons(long enrollmentId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.LessonsAsync(enrollmentId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
}
