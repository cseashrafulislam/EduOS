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
[Route("api/public/credentials")]
public sealed class PublicCertificateVerificationApiController : ControllerBase
{
    private readonly IPublicCertificateVerificationService _service;
    public PublicCertificateVerificationApiController(IPublicCertificateVerificationService service)
        => _service = service;

    [HttpGet("transcripts/{reference:guid}")]
    public async Task<IActionResult> VerifyTranscript(Guid reference, CancellationToken ct)
    {
        var response = await _service.VerifyTranscriptAsync(reference, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("certificates/{reference:guid}")]
    public async Task<IActionResult> Verify(Guid reference, CancellationToken ct)
    {
        var response = await _service.VerifyAsync(reference, ct);
        return StatusCode(response.StatusCode, response);
    }
}
