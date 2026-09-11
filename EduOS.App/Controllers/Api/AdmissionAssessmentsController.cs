using EduOS.App.Authorization;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,AdmissionOfficer")]
[RequireModule("ADMISSION")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("api/admission-assessments")]
public class AdmissionAssessmentsController : ControllerBase
{
    private readonly IAdmissionAssessmentService _service;

    public AdmissionAssessmentsController(IAdmissionAssessmentService service)
    {
        _service = service;
    }

    [HttpGet("tests")]
    public async Task<IActionResult> GetTests(CancellationToken cancellationToken)
    {
        var result = await _service.GetTestsAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("tests")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> CreateTest([FromBody] SaveAdmissionTestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CreateTestAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("tests/{id:long}")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> UpdateTest(long id, [FromBody] SaveAdmissionTestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.UpdateTestAsync(id, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("tests/{testId:long}/results")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> SaveResults(long testId, [FromBody] SaveAdmissionResultsDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SaveResultsAsync(testId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("tests/{testId:long}/merit")]
    public async Task<IActionResult> GetMeritList(long testId, CancellationToken cancellationToken)
    {
        var result = await _service.GetMeritListAsync(testId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("tests/{testId:long}/publish")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> PublishMeritList(long testId, CancellationToken cancellationToken)
    {
        var result = await _service.PublishMeritListAsync(testId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
