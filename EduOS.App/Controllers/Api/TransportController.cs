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

    [HttpGet("route-setup/campuses")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RouteSetupCampuses(CancellationToken ct) =>
        ToAction(await _service.GetRouteSetupCampusesAsync(ct));

    [HttpGet("route-setup")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RouteSetupPage([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRouteSetupPageAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpPost("route-setup")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CreateRouteWithStops([FromBody] CreateTransportRouteRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateRouteWithStopsAsync(request, ct));

    [HttpGet("vehicle-setup")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> VehicleSetupPage([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetVehicleSetupPageAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpPost("vehicle-setup")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CreateVehicleSetup([FromBody] CreateTransportVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateVehicleSetupAsync(request, ct));

    [HttpPut("vehicle-setup/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> UpdateVehicleSetup(Guid reference, [FromBody] UpdateTransportVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.UpdateVehicleSetupAsync(reference, request, ct));

    [HttpGet("route-vehicle-links")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RouteVehicleLinks([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRouteVehicleLinksAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpPost("route-vehicle-links")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> AssignRouteVehicle(
        [FromBody] CreateTransportRouteVehicleLinkRequestDto request, CancellationToken ct) =>
        ToAction(await _service.AssignRouteVehicleAsync(request, ct));

    [HttpPost("route-vehicle-links/{id:long}/close")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CloseRouteVehicle(long id,
        [FromBody] CloseTransportAssignmentRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CloseRouteVehicleAssignmentAsync(id, request, ct));

    [HttpGet("capacity-report")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CapacityReport([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetCapacityReportAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpGet("capacity-report/export")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CapacityExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportCapacityCsvAsync(campusId, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "transport-capacity.csv");
    }

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
