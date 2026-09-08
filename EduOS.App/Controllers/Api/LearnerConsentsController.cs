using EduOS.Core.DTOs.Student;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "Student,Parent")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("api/learner-consents")]
public sealed class LearnerConsentsController : ControllerBase
{
    private readonly ILearnerConsentService _service;

    public LearnerConsentsController(ILearnerConsentService service)
    {
        _service = service;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(CancellationToken cancellationToken)
    {
        var result = await _service.GetPendingAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("grants")]
    public async Task<IActionResult> GetActiveGrants(CancellationToken cancellationToken)
    {
        var result = await _service.GetActiveGrantsAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{requestReference:guid}/decision")]
    [EnableRateLimiting("LearnerIdentityPolicy")]
    public async Task<IActionResult> Resolve(
        Guid requestReference,
        [FromBody] ResolveLearnerConsentRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Consent decision is invalid." });

        var result = await _service.ResolveAsync(requestReference, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("grants/{grantReference:guid}/revoke")]
    [EnableRateLimiting("LearnerIdentityPolicy")]
    public async Task<IActionResult> Revoke(
        Guid grantReference,
        CancellationToken cancellationToken)
    {
        var result = await _service.RevokeAsync(grantReference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
