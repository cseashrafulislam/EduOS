using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "Student,Guardian,Parent")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/portal")]
public sealed class SelfServicePortalController : ControllerBase
{
    private readonly ISelfServicePortalService _service;
    private readonly IStudentDocumentService _documents;
    private readonly IStudentPhotoService _photos;
    public SelfServicePortalController(ISelfServicePortalService service, IStudentDocumentService documents,
        IStudentPhotoService photos)
    {
        _service = service;
        _documents = documents;
        _photos = photos;
    }

    [HttpGet("students")]
    public async Task<IActionResult> Students(CancellationToken ct) =>
        Result(await _service.GetLinkedStudentsAsync(ct));

    [HttpGet("students/{reference:guid}/timetable")]
    [RequireModule("ACADEMIC")]
    public async Task<IActionResult> Timetable(Guid reference, CancellationToken ct) =>
        Result(await _service.GetTimetableAsync(reference, ct));

    [HttpGet("students/{reference:guid}/attendance")]
    [RequireModule("ATTENDANCE")]
    public async Task<IActionResult> Attendance(Guid reference, [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetAttendanceAsync(reference, fromDate, toDate, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/results")]
    [RequireModule("EXAM")]
    public async Task<IActionResult> Results(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetResultsAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/fees")]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> Fees(Guid reference, CancellationToken ct) =>
        Result(await _service.GetFeesAsync(reference, ct));

    [HttpGet("students/{reference:guid}/invoices")]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> Invoices(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetInvoicesAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/payments")]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> Payments(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetPaymentsAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/transport")]
    [RequireModule("TRANSPORT")]
    public async Task<IActionResult> Transport(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetTransportAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/homework")]
    [RequireModule("LMS")]
    public async Task<IActionResult> Homework(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetHomeworkAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/assignments")]
    [RequireModule("LMS")]
    public async Task<IActionResult> Assignments(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetAssignmentsAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/photo")]
    [RequireModule("STUDENT")]
    public async Task<IActionResult> PortalPhoto(Guid reference, CancellationToken ct) =>
        Result(await _photos.GetPortalAsync(reference, ct));

    [HttpPost("students/{reference:guid}/photo")]
    [RequireModule("STUDENT")]
    [AutoValidateAntiforgeryToken]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3145728)]
    public async Task<IActionResult> UploadPortalPhoto(Guid reference, [FromForm] IFormFile? file,
        [FromForm] Guid requestId, [FromForm] string? rowVersion, CancellationToken ct)
    {
        if (file == null || file.Length is < 12 or > 2097152)
            return BadRequest(new { success = false, message = "Choose a JPG, PNG or WebP image up to 2 MB." });
        await using var stream = file.OpenReadStream();
        var result = await _photos.UploadPortalAsync(reference, requestId, rowVersion,
            new EduOS.Core.DTOs.Files.PrivateFileUploadDto
            {
                FileName = file.FileName, ContentType = file.ContentType,
                Length = file.Length, Content = stream
            }, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("students/{reference:guid}/photo/{assetReference:guid}")]
    [RequireModule("STUDENT")]
    public async Task<IActionResult> DownloadPortalPhoto(Guid reference, Guid assetReference, CancellationToken ct)
    {
        var result = await _photos.DownloadPortalAsync(reference, assetReference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType);
    }

    [HttpGet("document-types")]
    [RequireModule("STUDENT")]
    public async Task<IActionResult> PortalDocumentTypes(CancellationToken ct) =>
        Result(await _documents.GetPortalTypesAsync(ct));

    [HttpPost("students/{reference:guid}/documents")]
    [RequireModule("STUDENT")]
    [AutoValidateAntiforgeryToken]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6291456)]
    public async Task<IActionResult> UploadDocument(Guid reference, [FromForm] IFormFile? file,
        [FromForm] Guid requestId, [FromForm] string? documentTypeCode,
        [FromForm] string? title, CancellationToken ct)
    {
        if (file == null || file.Length is < 1 or > 5242880)
            return BadRequest(new { success = false, message = "Choose a file up to 5 MB." });
        await using var stream = file.OpenReadStream();
        var upload = new EduOS.Core.DTOs.Files.PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        };
        var result = await _documents.UploadPortalAsync(reference, requestId,
            documentTypeCode ?? string.Empty, title, upload, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("students/{reference:guid}/documents")]
    [RequireModule("STUDENT")]
    public async Task<IActionResult> Documents(Guid reference, CancellationToken ct) =>
        Result(await _documents.GetPortalAsync(reference, ct));

    [HttpGet("students/{reference:guid}/documents/{documentReference:guid}/download")]
    [RequireModule("STUDENT")]
    public async Task<IActionResult> DownloadDocument(Guid reference, Guid documentReference, CancellationToken ct)
    {
        var result = await _documents.DownloadPortalAsync(reference, documentReference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    private IActionResult Result<T>(EduOS.Core.Common.ApiResponse<T> response) =>
        StatusCode(response.StatusCode, response);
}
