using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/lms/teacher-cpd")]
[Authorize(Roles = "Teacher")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherCpdController(TeacherCpdService service, ILmsWorkflowService workflow) : ControllerBase
{
    [HttpGet("courses")]
    public async Task<IActionResult> Courses([FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, CancellationToken ct = default)
    {
        var result = await service.CoursesAsync(search, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll([FromBody] TeacherCpdEnrollRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.EnrollAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("enrollments/{id:long}/lessons")]
    public async Task<IActionResult> Lessons(long id, CancellationToken ct)
    {
        var result = await service.LessonsAsync(id, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("enrollments/{id:long}/lessons")]
    public async Task<IActionResult> UpdateLesson(long id,
        [FromBody] TeacherCpdLessonRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.SaveLessonAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("enrollments/{id:long}/complete")]
    public async Task<IActionResult> Complete(long id,
        [FromBody] CompleteCourseEnrollmentRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await workflow.CompleteCourseEnrollmentAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
