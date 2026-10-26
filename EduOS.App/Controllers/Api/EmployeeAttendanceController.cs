using EduOS.App.Authorization;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Attendance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/hr/employee-attendance")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("HR")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmployeeAttendanceController : ControllerBase
{
    private readonly IEmployeeAttendanceService service;
    public EmployeeAttendanceController(IEmployeeAttendanceService implementation) => service = implementation;
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> result) => StatusCode(result.StatusCode, result);
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? employeeReference, [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.GetAttendanceAsync(employeeReference, fromDate, toDate, page, pageSize, ct));
    [HttpPost]
    public async Task<IActionResult> Record([FromBody] SaveEmployeeAttendanceRequestDto request, CancellationToken ct)
        => Reply(await service.RecordAttendanceAsync(request, ct));
    [HttpPost("{attendanceId:long}/correct")]
    public async Task<IActionResult> Correct(long attendanceId, [FromBody] AttendanceCorrectionRequestDto request, CancellationToken ct)
        => Reply(await service.CorrectAttendanceAsync(attendanceId, request, ct));
    [HttpGet("{attendanceId:long}/corrections")]
    public async Task<IActionResult> Corrections(long attendanceId, CancellationToken ct)
        => Reply(await service.GetCorrectionsAsync(attendanceId, ct));
}
