using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-transfer-requests")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentTransferRequestsController : ControllerBase
{
    private readonly IStudentTransferRequestService _service;

    public StudentTransferRequestsController(IStudentTransferRequestService implementation) => _service = implementation;

    [HttpGet("students/{studentReference:guid}/eligibility")]
    public async Task<IActionResult> Eligibility(Guid studentReference, CancellationToken ct)
    {
        var result = await _service.GetEligibilityAsync(studentReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("students/{studentReference:guid}/requests")]
    public async Task<IActionResult> Requests(Guid studentReference, CancellationToken ct)
    {
        var result = await _service.GetRequestsAsync(studentReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitStudentTransferRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.SubmitAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{transferReference:guid}/review")]
    public async Task<IActionResult> Review(Guid transferReference, [FromBody] ReviewTransferRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ReviewAsync(transferReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("destination/inbox")]
    public async Task<IActionResult> DestinationInbox(CancellationToken ct)
    {
        var result = await _service.GetDestinationInboxAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("destination/{transferReference:guid}/decision")]
    public async Task<IActionResult> DestinationDecision(Guid transferReference,
        [FromBody] DestinationTransferDecisionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.DecideDestinationAsync(transferReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("destination/{transferReference:guid}/enrollment")]
    public async Task<IActionResult> LinkDestinationEnrollment(Guid transferReference,
        [FromBody] DestinationTransferEnrollmentDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.LinkDestinationEnrollmentAsync(transferReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("destination/{transferReference:guid}/confirm-enrollment")]
    public async Task<IActionResult> ConfirmDestinationEnrollment(Guid transferReference,
        [FromBody] DestinationTransferConfirmationDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ConfirmDestinationEnrollmentAsync(transferReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{transferReference:guid}/complete")]
    public async Task<IActionResult> Complete(Guid transferReference,
        [FromBody] CompleteTransferRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CompleteAsync(transferReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
