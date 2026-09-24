using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/academic-instruction")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AcademicInstructionController : ControllerBase
{
    private readonly IAcademicInstructionService _service;

    public AcademicInstructionController(IAcademicInstructionService service) => _service = service;

    [HttpGet("substitutions")]
    public async Task<IActionResult> Substitutions([FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, [FromQuery] long? academicBatchId, CancellationToken cancellationToken) =>
        ToAction(await _service.GetSubstitutionsAsync(fromDate, toDate, academicBatchId, cancellationToken));

    [HttpPost("substitutions")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateSubstitution([FromBody] CreateRoutineSubstitutionDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateSubstitutionAsync(request, cancellationToken));

    [HttpPost("substitutions/{id:long}/cancel")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CancelSubstitution(long id, [FromBody] CancelRoutineSubstitutionDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CancelSubstitutionAsync(id, request, cancellationToken));

    [HttpGet("lesson-plans")]
    public async Task<IActionResult> LessonPlans([FromQuery] long? academicBatchId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, CancellationToken cancellationToken) =>
        ToAction(await _service.GetLessonPlansAsync(academicBatchId, fromDate, toDate, cancellationToken));

    [HttpPost("lesson-plans")]
    public async Task<IActionResult> CreateLessonPlan([FromBody] CreateLessonPlanDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateLessonPlanAsync(request, cancellationToken));

    [HttpPost("lesson-plans/{id:long}")]
    public async Task<IActionResult> UpdateLessonPlan(long id, [FromBody] UpdateLessonPlanDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateLessonPlanAsync(id, request, cancellationToken));

    [HttpPost("lesson-plans/{id:long}/submit")]
    public async Task<IActionResult> SubmitLessonPlan(long id, [FromBody] AcademicRowVersionDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.SubmitLessonPlanAsync(id, request, cancellationToken));

    [HttpPost("lesson-plans/{id:long}/review")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> ReviewLessonPlan(long id, [FromBody] LessonPlanReviewDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.ReviewLessonPlanAsync(id, request, cancellationToken));

    [HttpPost("lesson-plans/{id:long}/progress")]
    public async Task<IActionResult> RecordLessonProgress(long id, [FromBody] LessonPlanProgressDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.RecordLessonProgressAsync(id, request, cancellationToken));

    private IActionResult ToAction<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
