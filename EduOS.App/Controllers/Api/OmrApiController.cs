using EduOS.App.Authorization;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Omr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/assessment-omr")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher,ExamController")]
[RequireModule("EXAM")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OmrApiController(OmrWorkflowService service, IDocumentFileService documents) : ControllerBase
{
    [HttpPost("templates")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateOmrTemplateRequestDto request,
        CancellationToken ct)
    {
        var result = await service.CreateTemplateAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("templates")]
    public async Task<IActionResult> Templates([FromQuery] long componentId, CancellationToken ct)
    {
        var result = await service.TemplatesAsync(componentId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("templates/{reference:guid}/print")]
    public async Task<IActionResult> PrintTemplate(Guid reference, CancellationToken ct)
    {
        var result = await service.PrintTemplateAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "image/svg+xml", $"EduOS-OMR-{reference:D}.svg");
    }

    [HttpPost("images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6_291_456)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file,
        [FromForm] Guid requestId, CancellationToken ct)
    {
        if (file == null || file.Length is < 1 or > 5_242_880 || requestId == Guid.Empty)
            return BadRequest(new { success = false, message = "Select a PNG/JPEG scan up to 5 MB." });
        await using var stream = file.OpenReadStream();
        var result = await documents.UploadForOmrAsync(requestId, new PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("sheets")]
    public async Task<IActionResult> Capture([FromBody] CaptureOmrSheetRequestDto request,
        CancellationToken ct)
    {
        var result = await service.CaptureAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("sheets")]
    public async Task<IActionResult> Sheets([FromQuery] Guid templateReference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
    {
        var result = await service.SearchAsync(templateReference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("sheets/{reference:guid}")]
    public async Task<IActionResult> Sheet(Guid reference, CancellationToken ct)
    {
        var result = await service.GetAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("sheets/{reference:guid}/review")]
    public async Task<IActionResult> Review(Guid reference, [FromBody] ReviewOmrSheetRequestDto request,
        CancellationToken ct)
    {
        var result = await service.ReviewAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("sheets/{reference:guid}/post")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> PostReviewed(Guid reference, [FromBody] PostOmrSheetRequestDto request,
        CancellationToken ct)
    {
        var result = await service.PostReviewedAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("exports")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> Export([FromBody] OmrExportRequestDto request, CancellationToken ct)
    {
        var result = await service.ExportAsync(request, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    [HttpGet("sheets/{reference:guid}/image")]
    public async Task<IActionResult> Download(Guid reference, CancellationToken ct)
    {
        var result = await service.DownloadAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}
