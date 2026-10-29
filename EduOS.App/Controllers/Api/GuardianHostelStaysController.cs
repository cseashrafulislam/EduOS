using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/hostel/guardian-stays")]
[Authorize(Roles = "Guardian,Parent")]
[RequireModule("HOSTEL")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuardianHostelStaysController(IGuardianHostelStayService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var result = await service.GetCurrentStaysAsync(page, pageSize, search, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int page = 1,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await service.ExportCurrentPageCsvAsync(page, search, ct);
        if (!result.Success || result.Data is null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "guardian-hostel-stays.csv");
    }
}
