using EduOS.App.Authorization;
using EduOS.Service.Services.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/ai/workspace")]
[Authorize(Roles = "Student,Teacher,Principal,TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiWorkspaceApiController(EduOsAiWorkspaceService service) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        success = true,
        data = new { modelConfigured = service.ProviderReady, readOnly = true }
    });

    [HttpPost("teacher")]
    [EnableRateLimiting("AiGenerationPolicy")]
    [Authorize(Roles = "Teacher,Principal,TenantAdmin")]
    [RequireModule("LMS")]
    public async Task<IActionResult> Teacher([FromBody] TeacherCopilotRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.TeacherAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("tutor")]
    [EnableRateLimiting("AiGenerationPolicy")]
    [Authorize(Roles = "Student")]
    [RequireModule("LMS")]
    public async Task<IActionResult> Tutor([FromBody] StudentTutorRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.TutorAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("management/campuses")]
    [Authorize(Roles = "Principal,TenantAdmin")]
    [RequireModule("ATTENDANCE")]
    public async Task<IActionResult> Campuses([FromQuery] string? search, CancellationToken ct)
    {
        var result = await service.CampusesAsync(search, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("management/summary")]
    [Authorize(Roles = "Principal,TenantAdmin")]
    [RequireModule("ATTENDANCE")]
    public async Task<IActionResult> Summary([FromQuery] long campusId,
        [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct)
    {
        var result = await service.SnapshotAsync(campusId, fromDate, toDate, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("management/interpret")]
    [EnableRateLimiting("AiGenerationPolicy")]
    [Authorize(Roles = "Principal,TenantAdmin")]
    [RequireModule("ATTENDANCE")]
    public async Task<IActionResult> Interpret([FromBody] AiManagementInterpretRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.InterpretAsync(request.CampusId, request.FromDate, request.ToDate, ct);
        return StatusCode(result.StatusCode, result);
    }
}

public sealed class AiManagementInterpretRequest
{
    [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)]
    public long CampusId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
}
