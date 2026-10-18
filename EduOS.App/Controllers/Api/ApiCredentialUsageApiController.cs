using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

/// <summary>Credential-only, read-only integration access; never grants an Identity session.</summary>
[ApiController]
[Route("api/integration/v1/usage")]
[AllowAnonymous]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ApiCredentialUsageApiController(IApiCredentialUsageService service) : ControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct = default)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Unauthorized(new { success = false, message = "Use a cookie-free API request." });
        var values = Request.Headers.Authorization;
        if (values.Count != 1)
            return Unauthorized(new { success = false, message = "Invalid API credential." });
        var result = await service.GetUsageAsync(values[0], 1, 100, ct);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data == null) return StatusCode(503, new { success = false, message = "Usage data is temporarily unavailable." });
        var csv = new System.Text.StringBuilder("PeriodStart,PeriodEnd,ActiveStudents,ActiveEmployees,ActiveCampuses\r\n");
        foreach (var row in result.Data.Items)
            csv.Append(FormattableString.Invariant(
                $"{row.PeriodStart:yyyy-MM-dd},{row.PeriodEnd:yyyy-MM-dd},{row.ActiveStudents},{row.ActiveEmployees},{row.ActiveCampuses}\r\n"));
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "no-store";
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv", "institution-usage-latest-100.csv");
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Unauthorized(new { success = false, message = "Use a cookie-free API request." });
        var values = Request.Headers.Authorization;
        if (values.Count != 1)
            return Unauthorized(new { success = false, message = "Invalid API credential." });
        var result = await service.GetUsageAsync(values[0], page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
}
