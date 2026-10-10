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
[EnableRateLimiting("ApiPolicy")]
public sealed class TransportController : ControllerBase
{
    private readonly ITransportService _service;
    public TransportController(ITransportService service) => _service = service;

    [HttpGet("routes")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Routes(CancellationToken ct) => ToAction(await _service.GetRoutesAsync(ct));

    [HttpGet("vehicles")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Vehicles(CancellationToken ct) => ToAction(await _service.GetVehiclesAsync(ct));

    [HttpGet("eligible-students")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> EligibleStudents([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetEligibleStudentsAsync(page, pageSize, search, cancellationToken));

    [HttpGet("active-assignments")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ActiveAssignments([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetActiveAssignmentsAsync(page, pageSize, search, cancellationToken));

    [HttpGet("my-assignment")]
    public async Task<IActionResult> MyAssignment(CancellationToken ct) => ToAction(await _service.GetMyAssignmentAsync(ct));

    [HttpPost("assignments")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Assign([FromBody] AssignStudentTransportRequestDto request, CancellationToken ct) => ToAction(await _service.AssignAsync(request, ct));

    [HttpPost("assignments/{reference:guid}/close")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Close(Guid reference, [FromBody] CloseStudentTransportRequestDto request, CancellationToken ct) => ToAction(await _service.CloseAsync(reference, request, ct));

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
