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

    [HttpGet("batches")]
    public async Task<IActionResult> GetBatchChoices([FromQuery] DateOnly? attendanceDate,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetBatchChoicesAsync(attendanceDate, search, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjectChoices([FromQuery] long academicBatchId,
        [FromQuery] DateOnly attendanceDate, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetSubjectChoicesAsync(
            academicBatchId, attendanceDate, search, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("roster")]
    public async Task<IActionResult> GetRoster([FromQuery] StudentAttendanceRosterQueryDto query, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GetRosterAsync(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("roster-page")]
    public async Task<IActionResult> GetRosterPage([FromQuery] StudentAttendanceRosterQueryDto query,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GetRosterPageAsync(query, page, pageSize, cancellationToken);
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
    
    [HttpPost("sessions/{sessionId:long}/finalize")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> FinalizeSession(long sessionId, [FromQuery] string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await _service.FinalizeSessionAsync(sessionId, rowVersion, cancellationToken);
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
    // Correction history is manager-only and deliberately separate from the teacher roster.
    [HttpGet("{attendanceId:long}/corrections")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> GetCorrections(long attendanceId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetCorrectionsAsync(attendanceId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
