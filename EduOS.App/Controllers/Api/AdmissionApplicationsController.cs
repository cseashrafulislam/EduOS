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
[Route("api/admission-applications")]
public class AdmissionApplicationsController : ControllerBase
{
    private readonly IAdmissionApplicationService _service;
    private readonly IAdmissionEnrollmentService _enrollmentService;

    public AdmissionApplicationsController(IAdmissionApplicationService service, IAdmissionEnrollmentService enrollmentService)
    {
        _service = service;
        _enrollmentService = enrollmentService;
    }

    [HttpGet("options")]
    public async Task<IActionResult> GetOptions(CancellationToken cancellationToken)
    {
        var result = await _service.GetOptionsAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] AdmissionApplicationQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _service.GetPageAsync(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Get(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _service.GetByReferenceAsync(reference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> Create([FromBody] CreateAdmissionApplicationDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CreateAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{reference:guid}/status")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> Review(Guid reference, [FromBody] ReviewAdmissionApplicationDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ReviewAsync(reference, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/enrollment-options")]
    public async Task<IActionResult> GetEnrollmentOptions(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _enrollmentService.GetOptionsAsync(reference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/admit")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> Admit(Guid reference, [FromBody] AdmitAdmissionApplicationDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _enrollmentService.AdmitAsync(reference, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
