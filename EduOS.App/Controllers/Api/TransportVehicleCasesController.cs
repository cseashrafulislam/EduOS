using EduOS.App.Authorization;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/transport/vehicle-cases")]
[Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportVehicleCasesController : ControllerBase
{
    private readonly ITransportVehicleCaseService _service;
    public TransportVehicleCasesController(ITransportVehicleCaseService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] long? campusId = null,
        [FromQuery] string? state = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await _service.GetAsync(campusId, state, search, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransportVehicleCaseRequestDto request,
        CancellationToken ct = default)
    {
        var result = await _service.CreateAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid reference,
        [FromBody] ResolveTransportVehicleCaseRequestDto request, CancellationToken ct = default)
    {
        var result = await _service.ResolveAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] long? campusId = null,
        [FromQuery] string? state = null, [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var result = await _service.ExportCsvAsync(campusId, state, search, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8",
            "transport-vehicle-cases.csv");
    }
}
