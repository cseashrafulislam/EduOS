using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/hostel")]
[Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
[RequireModule("HOSTEL")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
public sealed class HostelAllocationReadinessController : ControllerBase
{
    private readonly IHostelService _service;
    public HostelAllocationReadinessController(IHostelService service) => _service = service;

    [HttpGet("allocation-readiness")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Readiness([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] bool attentionOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetAllocationReadinessAsync(campusId, page, pageSize, search, attentionOnly, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("allocation-readiness/export")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Export([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] bool attentionOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportAllocationReadinessCsvAsync(campusId, search, attentionOnly, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-allocation-readiness.csv");
    }
}
