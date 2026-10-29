using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "Student,Guardian,Parent")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/lms/certificate-sharing")]
public sealed class LmsCertificateSharingController(ILmsCertificateGuardianService service) : ControllerBase
{
    [HttpGet("{certificate:guid}/guardians")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Guardians(Guid certificate, CancellationToken ct)
    {
        var result = await service.GetEligibleGuardiansAsync(certificate, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{certificate:guid}/grants")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Grants(Guid certificate, CancellationToken ct)
    {
        var result = await service.GetActiveGrantsAsync(certificate, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("grants")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Grant([FromBody] GrantLmsCertificateGuardianRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.GrantAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("grants/{reference:guid}/revoke")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Revoke(Guid reference,
        [FromBody] RevokeLmsCertificateGuardianRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.RevokeAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Guardian,Parent")]
    public async Task<IActionResult> Mine([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.GetSharedCertificatesAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}")]
    [Authorize(Roles = "Guardian,Parent")]
    public async Task<IActionResult> Read(Guid reference, CancellationToken ct)
    {
        var result = await service.GetSharedCertificateAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
}
