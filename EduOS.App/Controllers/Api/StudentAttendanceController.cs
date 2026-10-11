using EduOS.App.Authorization;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher")]
[RequireModule("ATTENDANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/student-attendance")]
public sealed class StudentAttendanceController : ControllerBase
{
    private readonly IStudentAttendanceService _service;

    public StudentAttendanceController(IStudentAttendanceService service)
    {
        _service = service;
    }

    [HttpGet("roster")]
    public async Task<IActionResult> GetRoster([FromQuery] StudentAttendanceRosterQueryDto query, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GetRosterAsync(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> EnsureSession([FromBody] EnsureStudentAttendanceSessionDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.EnsureSessionAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveAttendanceRegisterRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SaveAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    
    [HttpPost("{attendanceId:long}/correct")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> Correct(long attendanceId, [FromBody] AttendanceCorrectionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CorrectAttendanceAsync(attendanceId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
