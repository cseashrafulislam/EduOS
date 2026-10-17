using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/communication/messages/{messageId:long}/attachments")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MessageAttachmentsApiController(IMessageAttachmentService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(long messageId, CancellationToken ct)
    {
        var result = await service.GetForMessageAsync(messageId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6291456)]
    public async Task<IActionResult> Upload(long messageId, [FromForm] IFormFile? file,
        [FromForm] Guid requestId, CancellationToken ct)
    {
        if (file == null || requestId == Guid.Empty || file.Length is < 1 or > 5242880)
            return BadRequest(new { success = false, message = "Choose a file up to 5 MB and a valid request ID." });
        await using var stream = file.OpenReadStream();
        var upload = new PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        };
        var result = await service.UploadAsync(messageId, requestId, upload, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/download")]
    public async Task<IActionResult> Download(long messageId, Guid reference, CancellationToken ct)
    {
        var result = await service.DownloadAsync(messageId, reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}
