using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.DTOs.Files;
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

    [HttpPost("assignments/{assignmentReference:guid}/enrollments/{enrollmentId:long}/files")]
    [Authorize(Roles = "Student")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6291456)]
    public async Task<IActionResult> Upload(Guid assignmentReference, long enrollmentId,
        [FromForm] IFormFile? file, [FromForm] Guid requestId, CancellationToken ct)
    {
        if (file == null || file.Length is < 1 or > 5242880 || requestId == Guid.Empty)
            return BadRequest(new { success = false, message = "Choose a file up to 5 MB." });
        await using var stream = file.OpenReadStream();
        var result = await _service.UploadSubmissionFileAsync(assignmentReference, enrollmentId, requestId,
            new PrivateFileUploadDto
            {
                FileName = file.FileName, ContentType = file.ContentType,
                Length = file.Length, Content = stream
            }, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("submissions/{submissionId:long}/files/{fileReference:guid}/download")]
    public async Task<IActionResult> Download(long submissionId, Guid fileReference, CancellationToken ct)
    {
        var result = await _service.DownloadSubmissionFileAsync(submissionId, fileReference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}
