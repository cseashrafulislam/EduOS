using EduOS.App.Authorization;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Student,Guardian,Parent")]
[RequireModule("ATTENDANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/student-leaves")]
public sealed class StudentLeaveController : ControllerBase
{
    private readonly IStudentLeaveService _service;
    public StudentLeaveController(IStudentLeaveService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? enrollmentReference,
        [FromQuery] LeaveState? state, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetApplicationsAsync(enrollmentReference, state, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Apply([FromBody] CreateStudentLeaveApplicationRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ApplyAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6291456)]
    public async Task<IActionResult> UploadAttachment([FromForm] Guid enrollmentReference,
        [FromForm] Guid requestId, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length is < 1 or > 5242880)
            return BadRequest(new { success = false, message = "Choose a supporting file up to 5 MB." });
        await using var stream = file.OpenReadStream();
        var upload = new PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        };
        var result = await _service.UploadAttachmentAsync(enrollmentReference, requestId, upload, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{applicationId:long}/attachment")]
    public async Task<IActionResult> DownloadAttachment(long applicationId, CancellationToken ct)
    {
        var result = await _service.DownloadAttachmentAsync(applicationId, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    [HttpPost("{applicationId:long}/cancel")]
    public async Task<IActionResult> Cancel(long applicationId,
        [FromBody] CancelStudentLeaveRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CancelAsync(applicationId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{applicationId:long}/review")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> Review(long applicationId,
        [FromBody] ReviewStudentLeaveRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ReviewAsync(applicationId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
