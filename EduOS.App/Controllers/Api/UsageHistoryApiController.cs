using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/usage-history")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsageHistoryApiController : ControllerBase
{
    private readonly IUsageHistoryService _service;

    public UsageHistoryApiController(IUsageHistoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetHistoryAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken ct = default)
    {
        var result = await _service.ExportHistoryCsvAsync(fromDate, toDate, ct);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data ?? string.Empty),
            "text/csv", "institution-usage-history.csv");
    }

    [HttpPost("capture")]
    public async Task<IActionResult> Capture(CancellationToken ct)
    {
        var result = await _service.CaptureTodayAsync(ct);
        return StatusCode(result.StatusCode, result);
    }
}
