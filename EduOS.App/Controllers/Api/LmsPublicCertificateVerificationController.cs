using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

/// <summary>Only the owning learner can opt in. No guardian consent is inferred.</summary>
[ApiController]
[Authorize(Roles = "Student")]
[RequireModule("LMS")]
[EnableRateLimiting("ApiPolicy")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/lms/certificate-verification")]
public sealed class LmsPublicCertificateVerificationController(
    ILmsPublicCertificateVerificationService service) : ControllerBase
{
    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Status(Guid reference, CancellationToken ct)
    {
        var result = await service.GetStatusAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/enable")]
    public async Task<IActionResult> Enable(Guid reference,
        [FromBody] LmsPublicVerificationChangeDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.EnableAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/disable")]
    public async Task<IActionResult> Disable(Guid reference,
        [FromBody] LmsPublicVerificationChangeDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.DisableAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}

/// <summary>Anonymous status-only lookup. Bearer token is POSTed, never placed in an HTTP URL.</summary>
[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("LearnerIdentityPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/public/lms-certificate-verification")]
public sealed class PublicLmsCertificateVerificationController(
    ILmsPublicCertificateVerificationService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Verify([FromBody] LmsPublicVerificationTokenDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return NotFound();
        var result = await service.VerifyAsync(request.Token, ct);
        return StatusCode(result.StatusCode, result);
    }
}
