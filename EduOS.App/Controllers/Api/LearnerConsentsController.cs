using EduOS.Core.DTOs.Learners;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "Student,Parent,Guardian")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("LearnerIdentityPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetHistoryAsync(page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{requestReference:guid}/decision")]
    public async Task<IActionResult> Resolve(
        Guid requestReference,
        [FromBody] ResolveLearnerIdentityConsentRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Consent decision is invalid." });

        var result = await _service.ResolveAsync(requestReference, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("grants/{grantReference:guid}/revoke")]
    public async Task<IActionResult> Revoke(
        Guid grantReference,
        CancellationToken cancellationToken)
    {
        var result = await _service.RevokeAsync(grantReference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
