using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/legal-documents")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LegalDocumentsApiController(ILegalAcceptanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var result = await service.GetRequiredDocumentsAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var result = await service.GetMyAcceptancesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id:long}/accept")]
    public async Task<IActionResult> Accept(long id, [FromBody] AcceptLegalDocumentRequestDto request, CancellationToken ct)
    {
        if (request == null || request.LegalDocumentId != id) return BadRequest(new { success = false, message = "Document mismatch." });
        var result = await service.AcceptAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("admin")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Published(CancellationToken ct)
    {
        var result = await service.GetPublishedDocumentsAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("admin")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Publish([FromBody] PublishLegalDocumentRequestDto request, CancellationToken ct)
    {
        var result = await service.PublishAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
