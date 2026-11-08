using EduOS.App.Authorization;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-support-directory")]
[Authorize]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentSupportDirectoryController(StudentSupportDirectoryService service) : ControllerBase
{
    [HttpGet("enrollments")]
    public async Task<IActionResult> Enrollments(string? search = null, bool financialOnly = false,
        CancellationToken ct = default)
    {
        var response = await service.EnrollmentsAsync(search, financialOnly, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("aid-rules")]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> AidRules(CancellationToken ct)
    {
        var response = await service.AidRulesAsync(ct);
        return StatusCode(response.StatusCode, response);
    }
}
