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

    [HttpPost]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Save([FromBody] SaveStudentAttendanceDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SaveAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
