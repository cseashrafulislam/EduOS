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

    private static string Csv(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
