using EduOS.App.Authorization;
using EduOS.Core.DTOs.Exams;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher,ExamController")]
[RequireModule("EXAM")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("api/exams/workflow")]
public sealed class ExamWorkflowController : ControllerBase
{
    private readonly IExamWorkflowService _service;
    public ExamWorkflowController(IExamWorkflowService service) => _service = service;

    [HttpGet("mark-roster")]
    public async Task<IActionResult> GetMarkRoster([FromQuery] ExamMarkRosterQueryDto query, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GetMarkRosterAsync(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("marks")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> SaveMarks([FromBody] SaveExamMarksDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SaveMarksAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("results/generate")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Generate([FromBody] ExamScopeDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GenerateResultsAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("results/publish")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Publish([FromBody] ExamScopeDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.PublishResultsAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("results")]
    public async Task<IActionResult> Results([FromQuery] ExamScopeDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GetResultsAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
