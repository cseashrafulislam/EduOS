using EduOS.App.Authorization;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/transport")]
[Authorize]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
public sealed class TransportController : ControllerBase
{
    private readonly ITransportService _service;
    public TransportController(ITransportService service) => _service = service;

    [HttpGet("routes")]
    public async Task<IActionResult> Routes(CancellationToken ct) => ToAction(await _service.GetRoutesAsync(ct));

    [HttpGet("vehicles")]
    public async Task<IActionResult> Vehicles(CancellationToken ct) => ToAction(await _service.GetVehiclesAsync(ct));

    [HttpGet("my-assignment")]
    public async Task<IActionResult> MyAssignment(CancellationToken ct) => ToAction(await _service.GetMyAssignmentAsync(ct));

    [HttpPost("assignments")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Assign([FromBody] AssignTransportDto request, CancellationToken ct) => ToAction(await _service.AssignAsync(request, ct));

    [HttpPost("assignments/{reference:guid}/close")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Close(Guid reference, [FromBody] CloseTransportDto request, CancellationToken ct) => ToAction(await _service.CloseAsync(reference, request, ct));

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
