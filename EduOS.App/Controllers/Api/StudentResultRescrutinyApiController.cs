using EduOS.App.Authorization;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "Student,Guardian,Parent")]
[RequireModule("EXAM")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/portal/students/{studentReference:guid}/rescrutiny")]
public sealed class StudentResultRescrutinyApiController : ControllerBase
{
    private readonly IAssessmentAdministrationService _service;
    public StudentResultRescrutinyApiController(IAssessmentAdministrationService service) =>
        _service = service;

    [HttpGet]
    public async Task<IActionResult> Cases(Guid studentReference,
        [FromQuery] long publicationId, [FromQuery] long? beforeId, CancellationToken ct) =>
        Result(await _service.SearchLinkedResultRescrutinyAsync(studentReference, publicationId, beforeId, ct));

    [HttpPost]
    public async Task<IActionResult> Apply(Guid studentReference,
        [FromBody] RequestResultRescrutinyDto request, CancellationToken ct)
    {
        if (request == null || studentReference == Guid.Empty ||
            request.StudentReference != studentReference)
            return BadRequest(new { success = false, message = "Student reference mismatch." });
        return Result(await _service.RequestResultRescrutinyAsync(request, ct));
    }

    private IActionResult Result<T>(EduOS.Core.Common.ApiResponse<T> result) =>
        StatusCode(result.StatusCode, result);
}
