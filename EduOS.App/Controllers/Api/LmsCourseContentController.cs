using EduOS.App.Authorization;
using EduOS.Core.Common;
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
[Route("api/lms/course-content")]
[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsCourseContentController : ControllerBase
{
    private readonly ILmsCourseContentService _service;
    public LmsCourseContentController(ILmsCourseContentService implementation) => _service = implementation;

    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet("{courseReference:guid}")]
    public async Task<IActionResult> Course(Guid courseReference, CancellationToken ct) =>
        Reply(await _service.GetCourseContentAsync(courseReference, ct));

    [HttpPost("sections")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> CreateSection([FromBody] SaveCourseSectionRequestDto request, CancellationToken ct) =>
        Reply(await _service.SaveSectionAsync(null, request, ct));

    [HttpPut("sections/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> UpdateSection(long id, [FromBody] SaveCourseSectionRequestDto request, CancellationToken ct) =>
        Reply(await _service.SaveSectionAsync(id, request, ct));

    [HttpPost("lessons/section")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> AssignLesson([FromBody] AssignLessonSectionRequestDto request, CancellationToken ct) =>
        Reply(await _service.AssignLessonSectionAsync(request, ct));

    [HttpPost("lessons/{lessonReference:guid}/files")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6291456)]
    public async Task<IActionResult> UploadLessonFile(Guid lessonReference, [FromForm] IFormFile? file,
        [FromForm] Guid requestId, CancellationToken ct)
    {
        if (file == null || file.Length is < 1 or > 5242880 || requestId == Guid.Empty)
            return BadRequest(new { success = false, message = "Choose a file up to 5 MB." });
        await using var stream = file.OpenReadStream();
        var uploaded = await _service.UploadResourceFileAsync(lessonReference, requestId, new PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        }, ct);
        return StatusCode(uploaded.StatusCode, uploaded);
    }

    [HttpGet("resources/{id:long}/download")]
    public async Task<IActionResult> DownloadResource(long id, CancellationToken ct)
    {
        var result = await _service.DownloadResourceFileAsync(id, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    [HttpPost("resources")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> CreateResource([FromBody] SaveLessonResourceRequestDto request, CancellationToken ct) =>
        Reply(await _service.SaveResourceAsync(null, request, ct));

    [HttpPut("resources/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> UpdateResource(long id, [FromBody] SaveLessonResourceRequestDto request, CancellationToken ct) =>
        Reply(await _service.SaveResourceAsync(id, request, ct));

    [HttpPost("resources/{id:long}/remove")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> RemoveResource(long id, [FromBody] RemoveLessonResourceRequestDto request, CancellationToken ct) =>
        Reply(await _service.RemoveResourceAsync(id, request.RowVersion, ct));
}
