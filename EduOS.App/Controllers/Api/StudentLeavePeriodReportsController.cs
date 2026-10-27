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
[Route("api/student-leaves/report")]
public sealed class StudentLeavePeriodReportsController(IStudentLeavePeriodReportService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] StudentLeavePeriodReportQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GetAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }
}
