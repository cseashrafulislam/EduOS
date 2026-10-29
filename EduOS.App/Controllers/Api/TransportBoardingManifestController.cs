using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

/// <summary>Authorized daily pickup/drop planning only; not an attendance or trip record.</summary>
[ApiController]
[Route("api/transport/boarding-manifest")]
[Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportBoardingManifestController : ControllerBase
{
    private readonly ITransportService _service;
    public TransportBoardingManifestController(ITransportService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly operatingDate,
        [FromQuery] long? campusId = null, [FromQuery] Guid? routeReference = null,
        [FromQuery] Guid? vehicleReference = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null,
        [FromQuery] bool attentionOnly = false, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetBoardingManifestAsync(operatingDate, campusId,
            routeReference, vehicleReference, page, pageSize, search, attentionOnly, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly operatingDate,
        [FromQuery] long? campusId = null, [FromQuery] Guid? routeReference = null,
        [FromQuery] Guid? vehicleReference = null, [FromQuery] string? search = null,
        [FromQuery] bool attentionOnly = false, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportBoardingManifestCsvAsync(operatingDate, campusId,
            routeReference, vehicleReference, search, attentionOnly, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data),
            "text/csv; charset=utf-8", "transport-boarding-manifest.csv");
    }
}
