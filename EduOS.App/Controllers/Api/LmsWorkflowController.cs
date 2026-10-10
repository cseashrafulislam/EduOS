using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/lms")]
public sealed class LmsWorkflowController : ControllerBase
{
    private readonly ILmsWorkflowService _service;
    public LmsWorkflowController(ILmsWorkflowService service) => _service = service;

    [HttpPost("courses")]
    [HttpPut("courses/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> SaveCourse(Guid? reference, [FromBody] SaveCourseRequestDto request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SaveCourseAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("lessons")]
    [HttpPut("lessons/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> SaveLesson(Guid? reference, [FromBody] SaveLessonRequestDto request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SaveLessonAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("assignments")]
    [HttpPut("assignments/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> SaveAssignment(Guid? reference, [FromBody] SaveAssignmentRequestDto request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SaveAssignmentAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("courses/enrollments")]
    [Authorize(Roles = "TenantAdmin,Principal,Student")]
    public async Task<IActionResult> EnrollStudent([FromBody] EnrollCourseRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.EnrollStudentAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("instructors")]
    [Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> SearchInstructors([FromQuery] string search, [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        var result = await _service.SearchInstructorsAsync(search ?? string.Empty, take, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("courses")]
    public async Task<IActionResult> GetMyCourses([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _service.GetMyCoursesAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("courses/{reference:guid}")]
    public async Task<IActionResult> GetCourse(Guid reference, CancellationToken ct)
    {
        var result = await _service.GetCourseDetailsAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("assignments/submit")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Submit([FromBody] SubmitAssignmentRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SubmitAssignmentAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("assignments/submissions/{submissionId:long}/grade")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Grade(long submissionId,
        [FromBody] GradeAssignmentSubmissionRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GradeSubmissionAsync(submissionId, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("lessons/progress")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> UpdateProgress([FromBody] UpdateLessonProgressRequestDto request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.UpdateLessonProgressAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
