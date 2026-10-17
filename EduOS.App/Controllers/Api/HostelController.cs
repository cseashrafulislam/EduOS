using EduOS.App.Authorization;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/hostel")]
[Authorize]
[RequireModule("HOSTEL")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
public sealed class HostelController : ControllerBase
{
    private readonly IHostelService _service;
    public HostelController(IHostelService service) => _service = service;

    [HttpGet("facility-campuses")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> FacilityCampuses(CancellationToken ct) =>
        ToAction(await _service.GetFacilityCampusesAsync(ct));

    [HttpGet("facilities")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Facilities([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken ct = default) =>
        ToAction(await _service.GetFacilitiesAsync(campusId, page, pageSize, search, ct));

    [HttpPost("facilities")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> CreateFacility([FromBody] CreateHostelFacilityRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateFacilityAsync(request, ct));

    [HttpPut("facilities/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> UpdateFacility(Guid reference, [FromBody] UpdateHostelFacilityRequestDto request, CancellationToken ct) =>
        ToAction(await _service.UpdateFacilityAsync(reference, request, ct));

    [HttpPut("facilities/{reference:guid}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> SetFacilityState(Guid reference, [FromBody] SetHostelFacilityStateRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetFacilityStateAsync(reference, request, ct));

    [HttpPost("rooms")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> CreateRoom([FromBody] CreateHostelRoomRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateRoomAsync(request, ct));

    [HttpGet("room-inventory")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RoomInventory([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRoomInventoryAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpPut("rooms/{id:long}/capacity")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> SetRoomCapacity(long id, [FromBody] SetHostelRoomCapacityRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetRoomCapacityAsync(id, request, ct));

    [HttpPut("rooms/{id:long}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> SetRoomState(long id, [FromBody] SetHostelRoomStateRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetRoomStateAsync(id, request, ct));

    [HttpPost("beds")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> CreateBed([FromBody] CreateHostelBedRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateBedAsync(request, ct));

    [HttpGet("bed-inventory")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> BedInventory([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetBedInventoryAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpPut("beds/{id:long}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> SetBedState(long id, [FromBody] SetHostelBedStateRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetBedStateAsync(id, request, ct));

    [HttpGet("rooms")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> Rooms(CancellationToken ct) => ToAction(await _service.GetRoomsAsync(ct));

    [HttpGet("capacity")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Capacity([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetCapacityAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpGet("occupancy-insights")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> OccupancyInsights([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetOccupancyInsightsAsync(campusId, search, cancellationToken));

    [HttpGet("capacity/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportCapacity([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportCapacityCsvAsync(campusId, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-capacity.csv");
    }

    [HttpGet("eligible-students")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> EligibleStudents([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetEligibleStudentsAsync(page, pageSize, search, cancellationToken));

    [HttpGet("available-beds")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> AvailableBeds([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetAvailableBedsAsync(page, pageSize, search, cancellationToken));

    [HttpGet("active-allocations")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> ActiveAllocations([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetActiveAllocationsAsync(page, pageSize, search, cancellationToken));

    [HttpGet("allocation-history")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AllocationHistory([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null,
        [FromQuery] EduOS.Core.Enums.Domain.HostelAllocationState? state = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetAllocationHistoryAsync(campusId, page, pageSize, search, state, fromDate, toDate, cancellationToken));

    [HttpGet("my-allocation")]
    public async Task<IActionResult> MyAllocation(CancellationToken ct) => ToAction(await _service.GetMyAllocationAsync(ct));

    [HttpPost("allocations")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> Allocate([FromBody] AllocateStudentHostelRequestDto request, CancellationToken ct) => ToAction(await _service.AllocateAsync(request, ct));

    [HttpPost("allocations/{id:long}/close")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> Close(long id, [FromBody] CloseStudentHostelAllocationRequestDto request, CancellationToken ct) => ToAction(await _service.CloseAsync(id, request, ct));

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
