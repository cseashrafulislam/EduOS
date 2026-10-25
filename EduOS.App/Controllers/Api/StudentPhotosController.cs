using EduOS.App.Authorization;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/students/{studentReference:guid}/photo")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentPhotosController(IStudentPhotoService service) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3145728)]
    public async Task<IActionResult> Upload(Guid studentReference, [FromForm] IFormFile? file,
        [FromForm] Guid requestId, [FromForm] string? rowVersion, CancellationToken ct)
    {
        if (file == null || file.Length is < 12 or > 2097152)
            return BadRequest(new { success = false, message = "Choose a JPG, PNG or WebP photo up to 2 MB." });
        await using var stream = file.OpenReadStream();
        var result = await service.UploadAsync(studentReference, requestId, rowVersion,
            new PrivateFileUploadDto
            {
                FileName = file.FileName, ContentType = file.ContentType,
                Length = file.Length, Content = stream
            }, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{assetReference:guid}")]
    public async Task<IActionResult> Download(Guid studentReference, Guid assetReference, CancellationToken ct)
    {
        var result = await service.DownloadAsync(studentReference, assetReference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType);
    }
}
