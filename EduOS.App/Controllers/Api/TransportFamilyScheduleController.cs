using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/transport/family-schedule")]
[Authorize(Roles = "Student,Guardian,Parent")]
[RequireModule("TRANSPORT")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportFamilyScheduleController : ControllerBase
{
    private readonly ITransportFamilyScheduleService _service;
    public TransportFamilyScheduleController(ITransportFamilyScheduleService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly? operatingDate = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetAsync(operatingDate, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly? operatingDate = null,
        CancellationToken ct = default)
    {
        var result = await _service.ExportCsvAsync(operatingDate, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8",
            "my-transport-schedule.csv");
    }
}
