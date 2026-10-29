using EduOS.App.Authorization;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
namespace EduOS.App.Controllers.Api;
[ApiController]
[Route("api/transport/trips")]
[Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportTripsController : ControllerBase
{
    private readonly ITransportTripService _service;
    public TransportTripsController(ITransportTripService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly date,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await _service.GetAsync(date, campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Plan([FromBody] PlanTransportTripRequestDto request, CancellationToken ct)
    {
        var result = await _service.PlanAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/dispatch")]
    public async Task<IActionResult> Dispatch(Guid reference,
        [FromBody] DispatchTransportTripRequestDto request, CancellationToken ct)
    {
        var result = await _service.DispatchAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/riders")]
    public async Task<IActionResult> Riders(Guid reference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await _service.GetRidersAsync(reference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/export")]
    public async Task<IActionResult> Export(Guid reference, CancellationToken ct = default)
    {
        var result = await _service.ExportRidersCsvAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "transport-trip-riders.csv");
    }
}
