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
    public async Task<IActionResult> Substitutions([FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, [FromQuery] long? academicBatchId, CancellationToken cancellationToken) =>
        ToAction(await _service.GetSubstitutionsAsync(fromDate, toDate, academicBatchId, cancellationToken));

    [HttpPost("substitutions")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateSubstitution([FromBody] CreateSubstitutionRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateSubstitutionAsync(request, cancellationToken));

    [HttpPost("substitutions/{id:long}/cancel")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CancelSubstitution(long id, [FromBody] CancelSubstitutionRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CancelSubstitutionAsync(id, request, cancellationToken));

    [HttpGet("lesson-plans")]
    public async Task<IActionResult> LessonPlans([FromQuery] long? academicBatchId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetLessonPlansAsync(academicBatchId, fromDate, toDate, page, pageSize, cancellationToken));

    [HttpPost("lesson-plans")]
    public async Task<IActionResult> CreateLessonPlan([FromBody] SaveLessonPlanRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateLessonPlanAsync(request, cancellationToken));

    [HttpPost("lesson-plans/{id:long}")]
    public async Task<IActionResult> UpdateLessonPlan(long id, [FromBody] SaveLessonPlanRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateLessonPlanAsync(id, request, cancellationToken));

    [HttpPost("lesson-plans/{id:long}/submit")]
    public async Task<IActionResult> SubmitLessonPlan(long id, [FromBody] string rowVersion, CancellationToken cancellationToken) =>
        ToAction(await _service.SubmitLessonPlanAsync(id, rowVersion, cancellationToken));

    [HttpPost("lesson-plans/{id:long}/review")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> ReviewLessonPlan(long id, [FromBody] ReviewLessonPlanRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.ReviewLessonPlanAsync(id, request, cancellationToken));

    private IActionResult ToAction<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
