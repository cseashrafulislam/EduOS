using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/lms/adaptive")]
[Authorize(Roles = "Student,Teacher,Principal,TenantAdmin")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdaptiveStudyController(AdaptiveStudyService service) : ControllerBase
{
    [HttpGet("courses")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Courses(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.CoursesAsync(false, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("teacher/courses")]
    [Authorize(Roles = "Teacher,Principal,TenantAdmin")]
    public async Task<IActionResult> TeacherCourses(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.CoursesAsync(true, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("course/{courseReference:guid}/chapters")]
    public async Task<IActionResult> Chapters(Guid courseReference, CancellationToken ct)
    {
        var result = await service.ChaptersAsync(courseReference, false, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("teacher/course/{courseReference:guid}/chapters")]
    [Authorize(Roles = "Teacher,Principal,TenantAdmin")]
    public async Task<IActionResult> TeacherChapters(Guid courseReference, CancellationToken ct)
    {
        var result = await service.ChaptersAsync(courseReference, true, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("teacher/course/{courseReference:guid}/roster")]
    [Authorize(Roles = "Teacher,Principal,TenantAdmin")]
    public async Task<IActionResult> Roster(Guid courseReference, int page = 1,
        int pageSize = 30, CancellationToken ct = default)
    {
        var result = await service.RosterAsync(courseReference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("teacher/course/{courseReference:guid}/skills")]
    [Authorize(Roles = "Teacher,Principal,TenantAdmin")]
    public async Task<IActionResult> Skills(Guid courseReference, CancellationToken ct)
    {
        var result = await service.SkillsAsync(courseReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("teacher/course/{courseReference:guid}/goals/{studentReference:guid}")]
    [Authorize(Roles = "Teacher,Principal,TenantAdmin")]
    public async Task<IActionResult> Goals(Guid courseReference, Guid studentReference, CancellationToken ct)
    {
        var result = await service.GoalsAsync(courseReference, studentReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("teacher/goals")]
    [Authorize(Roles = "Teacher,Principal,TenantAdmin")]
    public async Task<IActionResult> SaveGoal([FromBody] SaveAdaptiveGoalDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.SaveGoalAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("course/{courseReference:guid}/plan")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Current(Guid courseReference, CancellationToken ct)
    {
        var result = await service.CurrentAsync(courseReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("plans")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Save([FromBody] SaveAdaptivePlanDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.SavePlanAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("course/{courseReference:guid}/history")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> History(Guid courseReference, int page = 1,
        int pageSize = 15, CancellationToken ct = default)
    {
        var result = await service.HistoryAsync(courseReference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("history/{id:long}")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> HistoryDetail(long id, CancellationToken ct)
    {
        var result = await service.HistoricalAsync(id, ct);
        return StatusCode(result.StatusCode, result);
    }
}
