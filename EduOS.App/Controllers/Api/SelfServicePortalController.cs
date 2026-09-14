using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "Student,Guardian,Parent")]
[ApiController]
[Route("api/portal")]
public sealed class SelfServicePortalController : ControllerBase
{
    private readonly ISelfServicePortalService _service;
    public SelfServicePortalController(ISelfServicePortalService service) => _service = service;

    [HttpGet("students")]
    public async Task<IActionResult> Students(CancellationToken cancellationToken) { var result = await _service.GetLinkedStudentsAsync(cancellationToken); return StatusCode(result.StatusCode, result); }

    [HttpGet("students/{reference:guid}/attendance")]
    [RequireModule("ATTENDANCE")]
    public async Task<IActionResult> Attendance(Guid reference, [FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, CancellationToken cancellationToken) { var result = await _service.GetAttendanceAsync(reference, fromDate, toDate, cancellationToken); return StatusCode(result.StatusCode, result); }

    [HttpGet("students/{reference:guid}/results")]
    [RequireModule("EXAM")]
    public async Task<IActionResult> Results(Guid reference, CancellationToken cancellationToken) { var result = await _service.GetResultsAsync(reference, cancellationToken); return StatusCode(result.StatusCode, result); }

    [HttpGet("students/{reference:guid}/fees")]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> Fees(Guid reference, CancellationToken cancellationToken) { var result = await _service.GetFeesAsync(reference, cancellationToken); return StatusCode(result.StatusCode, result); }
}
