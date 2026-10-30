using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "Student,Guardian,Parent")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/lms/progress-sharing")]
public sealed class LmsProgressSharingController(ILmsProgressGuardianService service) : ControllerBase
{
    [HttpGet("{enrollmentId:long}/guardians")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Guardians(long enrollmentId, CancellationToken ct)
    {
        var result = await service.EligibleAsync(enrollmentId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{enrollmentId:long}/grants")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Grants(long enrollmentId, CancellationToken ct)
    {
        var result = await service.GrantsAsync(enrollmentId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("grants")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Grant([FromBody] GrantLmsProgressGuardianRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GrantAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("grants/{reference:guid}/revoke")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Revoke(Guid reference,
        [FromBody] RevokeLmsProgressGuardianRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.RevokeAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Guardian,Parent")]
    public async Task<IActionResult> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.SharedAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("mine/{enrollmentId:long}/lessons")]
    [Authorize(Roles = "Guardian,Parent")]
    public async Task<IActionResult> Lessons(long enrollmentId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.LessonsAsync(enrollmentId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
}
