using EduOS.App.Authorization;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/hr/leave-entitlements")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("HR")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LeaveEntitlementsController : ControllerBase
{
    private readonly ILeaveEntitlementService service;
    // Central DI is Platform-owned; use the same explicit construction as HR Setup.
    public LeaveEntitlementsController(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
        => service = new LeaveEntitlementService(db, user, clock);
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
    [HttpGet("types")]
    public async Task<IActionResult> Types(CancellationToken ct) => Reply(await service.GetLeaveTypesAsync(ct));
    [HttpPost("types")]
    public async Task<IActionResult> CreateType([FromBody] SaveLeaveTypeRequestDto request, CancellationToken ct)
        => Reply(await service.SaveLeaveTypeAsync(null, request, ct));
    [HttpPut("types/{id:long}")]
    public async Task<IActionResult> UpdateType(long id, [FromBody] SaveLeaveTypeRequestDto request, CancellationToken ct)
        => Reply(await service.SaveLeaveTypeAsync(id, request, ct));
    [HttpGet("employees/{reference:guid}/entitlements")]
    public async Task<IActionResult> Entitlements(Guid reference, [FromQuery] int year, CancellationToken ct)
        => Reply(await service.GetEntitlementsAsync(reference, year, ct));
    [HttpPut("entitlements")]
    public async Task<IActionResult> SaveEntitlement([FromBody] SaveEmployeeLeaveEntitlementRequestDto request, CancellationToken ct)
        => Reply(await service.SaveEntitlementAsync(request, ct));
    [HttpPost("adjustments")]
    public async Task<IActionResult> Adjustment([FromBody] CreateEmployeeLeaveAdjustmentRequestDto request, CancellationToken ct)
        => Reply(await service.AddAdjustmentAsync(request, ct));
    [HttpGet("employees/{reference:guid}/balances")]
    public async Task<IActionResult> Balances(Guid reference, [FromQuery] int year, CancellationToken ct)
        => Reply(await service.GetBalancesAsync(reference, year, ct));
}
