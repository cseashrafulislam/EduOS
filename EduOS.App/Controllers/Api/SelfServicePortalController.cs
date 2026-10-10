using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "Student,Guardian,Parent")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/portal")]
public sealed class SelfServicePortalController : ControllerBase
{
    private readonly ISelfServicePortalService _service;
    public SelfServicePortalController(ISelfServicePortalService service) => _service = service;

    [HttpGet("students")]
    public async Task<IActionResult> Students(CancellationToken ct) =>
        Result(await _service.GetLinkedStudentsAsync(ct));

    [HttpGet("students/{reference:guid}/timetable")]
    [RequireModule("ACADEMIC")]
    public async Task<IActionResult> Timetable(Guid reference, CancellationToken ct) =>
        Result(await _service.GetTimetableAsync(reference, ct));

    [HttpGet("students/{reference:guid}/attendance")]
    [RequireModule("ATTENDANCE")]
    public async Task<IActionResult> Attendance(Guid reference, [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetAttendanceAsync(reference, fromDate, toDate, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/results")]
    [RequireModule("EXAM")]
    public async Task<IActionResult> Results(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetResultsAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/fees")]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> Fees(Guid reference, CancellationToken ct) =>
        Result(await _service.GetFeesAsync(reference, ct));

    [HttpGet("students/{reference:guid}/invoices")]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> Invoices(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetInvoicesAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/payments")]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> Payments(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetPaymentsAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/transport")]
    [RequireModule("TRANSPORT")]
    public async Task<IActionResult> Transport(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetTransportAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/homework")]
    [RequireModule("LMS")]
    public async Task<IActionResult> Homework(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetHomeworkAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/assignments")]
    [RequireModule("LMS")]
    public async Task<IActionResult> Assignments(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetAssignmentsAsync(reference, page, pageSize, ct));

    private IActionResult Result<T>(EduOS.Core.Common.ApiResponse<T> response) =>
        StatusCode(response.StatusCode, response);
}
