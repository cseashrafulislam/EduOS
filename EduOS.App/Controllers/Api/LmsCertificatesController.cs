using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/lms/certificates")]
public sealed class LmsCertificatesController : ControllerBase
{
    private readonly ILmsWorkflowService _service;
    public LmsCertificatesController(ILmsWorkflowService service) => _service = service;

    [HttpPost]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Issue([FromBody] IssueLmsCertificateRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.IssueCertificateAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/revoke")]
    [Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Revoke(Guid reference, [FromBody] RevokeLmsCertificateRequestDto request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.RevokeCertificateAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Mine([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetMyCertificatesAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("course/{courseReference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Course(Guid courseReference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetCourseCertificatesAsync(courseReference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Get(Guid reference, CancellationToken ct)
    {
        var result = await _service.GetCertificateAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/history")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> History(Guid reference, CancellationToken ct)
    {
        var result = await _service.GetCertificateAuditAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/verify")]
    public async Task<IActionResult> Verify(Guid reference, CancellationToken ct)
    {
        var result = await _service.VerifyCertificateAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
}
