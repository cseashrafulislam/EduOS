using EduOS.App.Authorization;
using EduOS.Core.Constants;
using System.ComponentModel.DataAnnotations;
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
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(new
    {
        success = true,
        data = new { modelConfigured = await service.IsCurrentRoleReadyAsync(ct), readOnly = true }
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
    [RequirePermission(PermissionConstants.Attendance.View)]
    public async Task<IActionResult> Campuses([FromQuery] string? search, CancellationToken ct)
    {
        var result = await service.CampusesAsync(search, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("management/summary")]
    [Authorize(Roles = "Principal,TenantAdmin")]
    [RequireModule("ATTENDANCE")]
    [RequirePermission(PermissionConstants.Attendance.View)]
    public async Task<IActionResult> Summary([FromQuery] long campusId,
        [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct)
    {
        var result = await service.SnapshotAsync(campusId, fromDate, toDate, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("management/ask")]
    [Authorize(Roles = "Principal,TenantAdmin")]
    [RequireModule("ATTENDANCE")]
    [RequirePermission(PermissionConstants.Attendance.View)]
    public async Task<IActionResult> Ask([FromBody] AiManagementAskRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request == null) return ValidationProblem(ModelState);
        var result = await service.AskVerifiedMetricAsync(
            request.CampusId, request.FromDate, request.ToDate, request.Question, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("management/interpret")]
    [EnableRateLimiting("AiGenerationPolicy")]
    [Authorize(Roles = "Principal,TenantAdmin")]
    [RequireModule("ATTENDANCE")]
    [RequirePermission(PermissionConstants.Attendance.View)]
    public async Task<IActionResult> Interpret([FromBody] AiManagementInterpretRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.InterpretAsync(request.CampusId, request.FromDate, request.ToDate, request.ConsentToExternalAi, ct);
        return StatusCode(result.StatusCode, result);
    }
}

public sealed class AiManagementAskRequest
{
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Required, StringLength(200, MinimumLength = 3)] public string Question { get; set; } = "";
}

public sealed class AiManagementInterpretRequest
{
    public bool ConsentToExternalAi { get; set; }
    [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)]
    public long CampusId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
}
