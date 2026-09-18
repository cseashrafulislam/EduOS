using EduOS.App.Authorization;
using EduOS.Core.DTOs.Portals;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "Teacher,Staff")]
[RequireModule("HR")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ApiController]
[Route("api/employee-portal")]
public sealed class EmployeeSelfServiceController : ControllerBase
{
    private readonly IEmployeeSelfServiceService _service;

    public EmployeeSelfServiceController(IEmployeeSelfServiceService service) => _service = service;

    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var result = await _service.GetProfileAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, CancellationToken cancellationToken)
    {
        var result = await _service.GetAttendanceAsync(fromDate, toDate, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("leave")]
    public async Task<IActionResult> Leave(CancellationToken cancellationToken)
    {
        var result = await _service.GetLeaveHistoryAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("leave/balances")]
    public async Task<IActionResult> LeaveBalances([FromQuery] int? year, CancellationToken cancellationToken)
    {
        var result = await _service.GetLeaveBalancesAsync(year, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("leave")]
    public async Task<IActionResult> ApplyLeave([FromBody] EmployeePortalLeaveApplyDto request, CancellationToken cancellationToken)
    {
        var result = await _service.ApplyLeaveAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
