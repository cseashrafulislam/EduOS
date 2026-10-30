using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/lms/assignment-attachments")]
[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsAssignmentAttachmentsController : ControllerBase
{
    private readonly ILmsCourseContentService _service;
    public LmsAssignmentAttachmentsController(ILmsCourseContentService implementation) => _service = implementation;

    [HttpGet("{assignmentReference:guid}")]
    public async Task<IActionResult> List(Guid assignmentReference, CancellationToken ct)
    {
        var response = await _service.GetAssignmentAttachmentsAsync(assignmentReference, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("{assignmentReference:guid}/files")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6291456)]
    public async Task<IActionResult> Upload(Guid assignmentReference, [FromForm] IFormFile? file,
        [FromForm] Guid requestId, CancellationToken ct)
    {
        if (file == null || file.Length is < 1 or > 5242880 || requestId == Guid.Empty)
            return BadRequest(new { success = false, message = "Choose a file up to 5 MB." });
        await using var stream = file.OpenReadStream();
        var response = await _service.UploadAssignmentAttachmentFileAsync(assignmentReference, requestId,
            new PrivateFileUploadDto
            {
                FileName = file.FileName, ContentType = file.ContentType,
                Length = file.Length, Content = stream
            }, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var response = await _service.DownloadAssignmentAttachmentAsync(id, ct);
        if (!response.Success || response.Data == null) return StatusCode(response.StatusCode, response);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox";
        return File(response.Data.Content, response.Data.ContentType, response.Data.FileName);
    }

    [HttpPost]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Create([FromBody] SaveAssignmentAttachmentRequestDto request, CancellationToken ct)
    {
        var response = await _service.SaveAssignmentAttachmentAsync(null, request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveAssignmentAttachmentRequestDto request, CancellationToken ct)
    {
        var response = await _service.SaveAssignmentAttachmentAsync(id, request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("{id:long}/remove")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Remove(long id, [FromBody] RemoveAssignmentAttachmentRequestDto request, CancellationToken ct)
    {
        var response = await _service.RemoveAssignmentAttachmentAsync(id, request.RowVersion, ct);
        return StatusCode(response.StatusCode, response);
    }
}
