using EduOS.App.Authorization;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/library/digital")]
[Authorize(Roles = "TenantAdmin,Principal,Librarian,Student,Teacher,Employee,Staff,Guardian,Parent")]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LibraryDigitalResourcesController(ILibraryDigitalResourceService service,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? bookReference = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] bool includeInactive = false,
        [FromQuery] string? category = null, [FromQuery] string? author = null,
        [FromQuery] string? publisher = null, [FromQuery] string? edition = null,
        [FromQuery] long? curriculumId = null, [FromQuery] long? subjectId = null,
        [FromQuery] string? chapter = null, [FromQuery] string? language = null,
        CancellationToken ct = default)
    {
        var result = await service.ListAsync(bookReference, search, page, pageSize, includeInactive, ct,
            category, author, publisher, edition, curriculumId, subjectId, chapter, language);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("study")]
    public async Task<IActionResult> Study([FromQuery] string? examCode = null,
        [FromQuery] string? boardCode = null, [FromQuery] int? syllabusYear = null,
        [FromQuery] int? previousPaperYear = null, [FromQuery] string? search = null,
        [FromQuery] long? subjectId = null, [FromQuery] string? chapter = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.ListAsync(null, search, page, pageSize, false, ct,
            subjectId: subjectId, chapter: chapter, examCode: examCode,
            boardCode: boardCode, syllabusYear: syllabusYear,
            previousPaperYear: previousPaperYear, studyOnly: true);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("study/bookmarks")]
    public async Task<IActionResult> StudyBookmarks([FromQuery] Guid[] references, CancellationToken ct)
    {
        var result = await service.GetStudyBookmarksAsync(references, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("study/bookmarks/{reference:guid}")]
    public async Task<IActionResult> StudyBookmark(Guid reference,
        [FromBody] LibraryStudyBookmarkRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request == null) return ValidationProblem(ModelState);
        var result = await service.SetStudyBookmarkAsync(reference, request.Bookmarked, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("study/progress/{reference:guid}")]
    public async Task<IActionResult> ReaderProgress(Guid reference, CancellationToken ct)
    {
        var result = await service.GetStudyReaderProgressAsync(reference, ct);
        Response.Headers.CacheControl = "no-store, private";
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("study/progress/token")]
    public IActionResult ProgressToken()
    {
        // Offline shell has no authenticated Razor layout. Same-origin cookie
        // authorization plus antiforgery still applies to its bounded sync writes.
        Response.Headers.CacheControl = "no-store, private";
        return Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [HttpPut("study/progress/{reference:guid}")]
    public async Task<IActionResult> SaveReaderProgress(Guid reference,
        [FromBody] SaveStudyReaderProgressDto request, CancellationToken ct)
    {
        if (request == null || !ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.SaveStudyReaderProgressAsync(reference, request, ct);
        Response.Headers.CacheControl = "no-store, private";
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("campuses")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Campuses(CancellationToken ct)
    {
        var result = await service.GetCampusOptionsAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("curriculum-options")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CurriculumOptions([FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await service.GetCurriculumOptionsAsync(search, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Export([FromQuery] Guid? bookReference = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] bool includeInactive = false,
        [FromQuery] string? category = null, [FromQuery] string? author = null,
        [FromQuery] string? publisher = null, [FromQuery] string? edition = null,
        [FromQuery] long? curriculumId = null, [FromQuery] long? subjectId = null,
        [FromQuery] string? chapter = null, [FromQuery] string? contentLanguage = null,
        [FromQuery] string language = "en-BD", CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD"))
            return BadRequest("Unsupported report language.");
        var result = await service.ListAsync(bookReference, search, page, pageSize, includeInactive, ct,
            category, author, publisher, edition, curriculumId, subjectId, chapter, contentLanguage);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryDigitalCsvWriter.Write(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "library-digital-page.csv");
    }

    [HttpGet("{reference:guid}/versions")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Versions(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.GetVersionHistoryAsync(reference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/versions/export")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ExportVersions(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string language = "en-BD",
        CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD"))
            return BadRequest("Unsupported report language.");
        var result = await service.GetVersionHistoryAsync(reference, page, pageSize, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryDigitalCsvWriter.WriteVersions(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "library-digital-versions-page.csv");
    }

    [HttpGet("audit")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Audit([FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, [FromQuery] string? action = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.GetAuditTrailAsync(from, to, action, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("audit/export")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ExportAudit([FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, [FromQuery] string? action = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string language = "en-BD", CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD"))
            return BadRequest("Unsupported report language.");
        var result = await service.GetAuditTrailAsync(from, to, action, page, pageSize, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryDigitalCsvWriter.WriteAudit(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "library-digital-audit-page.csv");
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

    [HttpPost("{reference:guid}/replace")]
    [Authorize(Roles = "TenantAdmin")]
    [RequestSizeLimit(6291456)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Replace(Guid reference,
        [FromForm] LibraryDigitalReplaceRequestDto request,
        [FromForm] IFormFile file, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (file == null || file.Length is < 1 or > 5242880)
            return BadRequest("A replacement file of at most 5 MB is required.");
        await using var stream = file.OpenReadStream();
        var upload = new PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        };
        var result = await service.ReplaceAsync(reference, request, upload, ct);
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

    [HttpPost("{reference:guid}/review")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Review(Guid reference,
        [FromBody] LibraryDigitalReviewRequestDto request, [FromQuery] bool approve,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.ReviewAsync(reference, request, approve, ct);
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

    [HttpGet("{reference:guid}/offline/permit")]
    public async Task<IActionResult> OfflinePermit(Guid reference, CancellationToken ct)
    {
        var result = await service.GetOfflinePermitAsync(reference, ct);
        Response.Headers.CacheControl = "no-store, private";
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/offline/chunk")]
    public async Task<IActionResult> OfflineChunk(Guid reference,
        [FromQuery] long offset, [FromQuery] string? sha256, CancellationToken ct)
    {
        var permission = await service.GetOfflinePermitAsync(reference, ct);
        if (!permission.Success || permission.Data == null)
            return StatusCode(permission.StatusCode, permission);
        var grant = permission.Data;
        if (offset < 0 || offset % grant.ChunkBytes != 0 || offset >= grant.SizeBytes ||
            !string.Equals(sha256, grant.Sha256, StringComparison.OrdinalIgnoreCase))
            return StatusCode(416, "The requested chunk does not match the current approved file.");
        // Each chunk reuses the canonical private read, including its post-storage
        // tenant, campus, expiry, malware and file-integrity reauthorization.
        var downloaded = await service.DownloadAsync(reference, ct);
        if (!downloaded.Success || downloaded.Data == null)
            return StatusCode(downloaded.StatusCode, downloaded);
        var refreshed = await service.GetOfflinePermitAsync(reference, ct);
        if (!refreshed.Success || refreshed.Data == null ||
            !string.Equals(grant.Sha256, refreshed.Data.Sha256, StringComparison.OrdinalIgnoreCase) ||
            downloaded.Data.Content.LongLength != grant.SizeBytes)
            return StatusCode(409, "Offline permission changed during download.");
        var length = (int)Math.Min(grant.ChunkBytes, grant.SizeBytes - offset);
        var buffer = new byte[length];
        Buffer.BlockCopy(downloaded.Data.Content, (int)offset, buffer, 0, length);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Range"] = $"bytes {offset}-{offset + length - 1}/{grant.SizeBytes}";
        Response.Headers["Accept-Ranges"] = "bytes";
        Response.Headers.ETag = $"\"{grant.Sha256}\"";
        Response.StatusCode = StatusCodes.Status206PartialContent;
        return File(buffer, "application/octet-stream");
    }

    [HttpGet("{reference:guid}/read")]
    public async Task<IActionResult> Read(Guid reference, CancellationToken ct)
    {
        // Reuse DownloadAsync's complete live tenant, campus, approval, expiry,
        // file-integrity and post-storage-read reauthorization checks.
        var result = await service.DownloadAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        if (result.Data.ContentType is not ("application/pdf" or "image/jpeg" or
            "image/png" or "image/webp"))
            return BadRequest("This file type can only be downloaded.");
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return File(result.Data.Content, result.Data.ContentType);
    }
}
