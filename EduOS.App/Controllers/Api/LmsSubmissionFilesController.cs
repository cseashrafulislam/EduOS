using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/lms/assignment-work")]
[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsSubmissionFilesController : ControllerBase
{
    private readonly ILmsWorkflowService _service;
    public LmsSubmissionFilesController(ILmsWorkflowService service) => _service = service;

    [HttpGet("mine")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Mine([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _service.GetMyAssignmentWorkAsync(page, pageSize, search, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("submissions/{submissionId:long}/files")]
    public async Task<IActionResult> Files(long submissionId, CancellationToken ct)
    {
        var result = await _service.GetSubmissionFilesAsync(submissionId, ct);
        return StatusCode(result.StatusCode, result);
    }
}
