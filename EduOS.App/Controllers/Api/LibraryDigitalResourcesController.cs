using EduOS.App.Authorization;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/library/digital")]
[Authorize(Roles = "TenantAdmin,Principal,Librarian,Student,Teacher,Employee,Staff")]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LibraryDigitalResourcesController(ILibraryDigitalResourceService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? bookReference = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var result = await service.ListAsync(bookReference, search, page, pageSize, includeInactive, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Export([FromQuery] Guid? bookReference = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] bool includeInactive = false,
        [FromQuery] string language = "en-BD", CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD"))
            return BadRequest("Unsupported report language.");
        var result = await service.ListAsync(bookReference, search, page, pageSize, includeInactive, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryDigitalCsvWriter.Write(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "library-digital-page.csv");
    }

    [HttpPost]
    [Authorize(Roles = "TenantAdmin")]
    [RequestSizeLimit(6291456)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] LibraryDigitalUploadRequestDto request,
        [FromForm] IFormFile file, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (file == null || file.Length is < 1 or > 5242880)
            return BadRequest("A file of at most 5 MB is required.");
        await using var stream = file.OpenReadStream();
        var upload = new PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        };
        var result = await service.UploadAsync(request, upload, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{reference:guid}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Update(Guid reference,
        [FromBody] LibraryDigitalMetadataRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.UpdateMetadataAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/revoke")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Revoke(Guid reference,
        [FromBody] LibraryDigitalRevokeRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.RevokeAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/download")]
    public async Task<IActionResult> Download(Guid reference, CancellationToken ct)
    {
        var result = await service.DownloadAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}
