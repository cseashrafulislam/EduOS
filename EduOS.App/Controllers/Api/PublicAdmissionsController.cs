using EduOS.Core.DTOs.Admission;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ApiController]
[Route("api/public/admissions/{tenantKey}")]
public sealed class PublicAdmissionsController : ControllerBase
{
    private readonly IPublicAdmissionService _service;

    public PublicAdmissionsController(IPublicAdmissionService service)
    {
        _service = service;
    }

    [HttpGet("options")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> GetOptions(string tenantKey, CancellationToken cancellationToken)
    {
        var result = await _service.GetOptionsAsync(tenantKey, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("applications")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> Create(string tenantKey, [FromBody] CreateAdmissionApplicationDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CreateAsync(tenantKey, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("applications/{reference:guid}/status")]
    [EnableRateLimiting("AdmissionIntakePolicy")]
    public async Task<IActionResult> GetStatus(string tenantKey, Guid reference, [FromQuery] PublicAdmissionStatusQueryDto query, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GetStatusAsync(tenantKey, reference, query.Mobile, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
