using EduOS.Core.DTOs.Admission;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("AdmissionIntakePolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/public/admissions/{tenantKey}")]
public sealed class PublicAdmissionsController : ControllerBase
{
    private readonly IPublicAdmissionService _service;

    public PublicAdmissionsController(IPublicAdmissionService service)
    {
        _service = service;
    }

    [HttpGet("forms")]
    public async Task<IActionResult> GetForms(string tenantKey, CancellationToken cancellationToken)
    {
        var result = await _service.GetFormsAsync(tenantKey, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("forms/{formReference:guid}")]
    public async Task<IActionResult> GetForm(string tenantKey, Guid formReference, CancellationToken cancellationToken)
    {
        var result = await _service.GetFormAsync(tenantKey, formReference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("options")]
    public async Task<IActionResult> GetOptions(string tenantKey, CancellationToken cancellationToken)
    {
        var result = await _service.GetOptionsAsync(tenantKey, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("applications")]
    public async Task<IActionResult> Create(string tenantKey, [FromBody] CreateAdmissionApplicationDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CreateAsync(tenantKey, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("applications/{reference:guid}/documents")]
    [RequestSizeLimit(10 * 1024 * 1024 + 64 * 1024)]
    public async Task<IActionResult> UploadDocument(string tenantKey, Guid reference, [FromForm] AdmissionDocumentUploadDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.UploadDocumentAsync(tenantKey, reference, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("applications/{reference:guid}/status")]
    public async Task<IActionResult> GetStatus(string tenantKey, Guid reference, [FromQuery] PublicAdmissionStatusQueryDto query, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.GetStatusAsync(tenantKey, reference, query.Mobile, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
