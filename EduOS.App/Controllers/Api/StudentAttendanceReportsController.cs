using EduOS.App.Authorization;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
[RequireModule("ATTENDANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/student-attendance/report")]
public sealed class StudentAttendanceReportsController(IStudentAttendanceReportService service) : ControllerBase
{
    [HttpGet("subjects")]
    public async Task<IActionResult> Subjects([FromQuery] long academicBatchId,
        [FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await service.GetSubjectChoicesAsync(academicBatchId, search, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("risk-summary")]
    public async Task<IActionResult> RiskSummary([FromQuery] StudentAttendanceReportQueryDto query,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GetRiskSummaryAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("subject-summary")]
    public async Task<IActionResult> SubjectSummary([FromQuery] StudentAttendanceReportQueryDto query,
        [FromQuery] string? subjectSearch, [FromQuery] string? subjectSort, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GetSubjectSummaryAsync(query, subjectSearch, subjectSort, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions([FromQuery] StudentAttendanceReportQueryDto query,
        [FromQuery] string? status, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GetSessionRegisterAsync(query, status, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] StudentAttendanceReportQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GetDailySummaryAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] StudentAttendanceReportQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GetAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }
}
