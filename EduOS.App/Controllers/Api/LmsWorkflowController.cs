using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;
[Authorize(Roles="TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("api/lms")]
public sealed class LmsWorkflowController:ControllerBase
{
    private readonly ILmsWorkflowService _service; public LmsWorkflowController(ILmsWorkflowService service)=>_service=service;
    [HttpPut("courses")][Authorize(Roles="TenantAdmin,Principal,Teacher")][EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> SaveCourse([FromBody]SaveCourseDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.SaveCourseAsync(r,ct);return StatusCode(x.StatusCode,x);}
    [HttpPut("lessons")][Authorize(Roles="TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> SaveLesson([FromBody]SaveLessonDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.SaveLessonAsync(r,ct);return StatusCode(x.StatusCode,x);}
    [HttpPut("assignments")][Authorize(Roles="TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> SaveAssignment([FromBody]SaveAssignmentDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.SaveAssignmentAsync(r,ct);return StatusCode(x.StatusCode,x);}
    [HttpPost("courses/{reference:guid}/sync-enrollment")][Authorize(Roles="TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Enroll(Guid reference,CancellationToken ct){var x=await _service.EnrollClassAsync(reference,ct);return StatusCode(x.StatusCode,x);}
    [HttpGet("courses")]
    public async Task<IActionResult> Courses(CancellationToken ct){var x=await _service.GetMyCoursesAsync(ct);return StatusCode(x.StatusCode,x);}
    [HttpGet("courses/{reference:guid}")]
    public async Task<IActionResult> Course(Guid reference,CancellationToken ct){var x=await _service.GetCourseAsync(reference,ct);return StatusCode(x.StatusCode,x);}
    [HttpPost("assignments/submit")][Authorize(Roles="Student")][EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Submit([FromBody]SubmitAssignmentDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.SubmitAssignmentAsync(r,ct);return StatusCode(x.StatusCode,x);}
    [HttpPost("assignments/review")][Authorize(Roles="TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Review([FromBody]ReviewSubmissionDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.ReviewSubmissionAsync(r,ct);return StatusCode(x.StatusCode,x);}
    [HttpPost("lessons/complete")][Authorize(Roles="Student")]
    public async Task<IActionResult> Complete([FromBody]CompleteLessonDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.CompleteLessonAsync(r,ct);return StatusCode(x.StatusCode,x);}
}
