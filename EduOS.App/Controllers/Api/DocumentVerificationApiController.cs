using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/document-verification")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DocumentVerificationApiController(IDocumentVerificationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Queue([FromQuery] string? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.GetReviewQueueAsync(status, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/review")]
    public async Task<IActionResult> Review(Guid reference, [FromBody] DocumentVerificationDecisionDto request, CancellationToken ct)
    {
        var result = await service.ReviewAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/preview")]
    public async Task<IActionResult> Preview(Guid reference, CancellationToken ct)
    {
        var result = await service.DownloadForReviewAsync(reference, ct);
        return Download(result);
    }

    [HttpGet("{reference:guid}/download")]
    public async Task<IActionResult> DownloadApproved(Guid reference, CancellationToken ct)
    {
        var result = await service.DownloadApprovedAsync(reference, ct);
        return Download(result);
    }

    private IActionResult Download(EduOS.Core.Common.ApiResponse<DocumentFileDownloadDto> result)
    {
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}