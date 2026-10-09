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

    [HttpGet("rooms")]
    public async Task<IActionResult> Rooms(CancellationToken ct) => ToAction(await _service.GetRoomsAsync(ct));

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

    [HttpGet("my-allocation")]
    public async Task<IActionResult> MyAllocation(CancellationToken ct) => ToAction(await _service.GetMyAllocationAsync(ct));

    [HttpPost("allocations")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> Allocate([FromBody] AllocateHostelDto request, CancellationToken ct) => ToAction(await _service.AllocateAsync(request, ct));

    [HttpPost("allocations/{id:long}/close")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> Close(long id, [FromBody] CloseHostelAllocationDto request, CancellationToken ct) => ToAction(await _service.CloseAsync(id, request, ct));

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
