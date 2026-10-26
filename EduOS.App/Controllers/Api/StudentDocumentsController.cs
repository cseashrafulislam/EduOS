using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-documents")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentDocumentsController(IStudentDocumentService service) : ControllerBase
{
    [HttpGet("types")]
    public async Task<IActionResult> Types(CancellationToken ct)
    {
        var result = await service.GetTypesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("students/{studentReference:guid}")]
    public async Task<IActionResult> List(Guid studentReference, CancellationToken ct)
    {
        var result = await service.GetAsync(studentReference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("students/{studentReference:guid}")]
    public async Task<IActionResult> Attach(Guid studentReference, [FromBody] AttachStudentDocumentDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.AttachAsync(studentReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("students/{studentReference:guid}/{documentReference:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid studentReference, Guid documentReference,
        [FromBody] DeactivateStudentDocumentDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.DeactivateAsync(studentReference, documentReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("students/{studentReference:guid}/{documentReference:guid}/download")]
    public async Task<IActionResult> Download(Guid studentReference, Guid documentReference, CancellationToken ct)
    {
        var result = await service.DownloadAsync(studentReference, documentReference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}
