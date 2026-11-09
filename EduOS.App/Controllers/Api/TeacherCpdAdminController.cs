using EduOS.App.Authorization;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/lms/teacher-cpd/admin")]
[Authorize(Roles = "TenantAdmin,Principal")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherCpdAdminController(TeacherCpdService service) : ControllerBase
{
    [HttpGet("courses")]
    public async Task<IActionResult> Courses([FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.ManagementCoursesAsync(search, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("courses/{reference:guid}/participants")]
    public async Task<IActionResult> Participants(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.ParticipantsAsync(reference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
}
