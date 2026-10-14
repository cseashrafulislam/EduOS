using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/document-admin")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DocumentAdministrationApiController(IDocumentService service) : ControllerBase
{
    [HttpGet("documents")]
    public async Task<IActionResult> Documents([FromQuery] string? entityType, [FromQuery] long? entityId,
        [FromQuery] string? documentTypeCode, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.GetDocumentsAsync(entityType, entityId, page, pageSize, ct, documentTypeCode, status, search);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("documents/{reference:guid}")]
    public async Task<IActionResult> Document(Guid reference, CancellationToken ct)
    {
        var result = await service.GetDocumentAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("documents")]
    public async Task<IActionResult> CreateDocument([FromBody] SaveDocumentRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveDocumentAsync(null, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPut("documents/{reference:guid}")]
    public async Task<IActionResult> UpdateDocument(Guid reference, [FromBody] SaveDocumentRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveDocumentAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("documents/{reference:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid reference, [FromBody] DeactivateDocumentRequest request, CancellationToken ct)
    {
        var result = await service.DeactivateDocumentAsync(reference, request.RowVersion, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("templates")]
    public async Task<IActionResult> Templates(CancellationToken ct)
    {
        var result = await service.GetTemplatesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] SaveDocumentTemplateRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveTemplateAsync(null, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPut("templates/{id:long}")]
    public async Task<IActionResult> UpdateTemplate(long id, [FromBody] SaveDocumentTemplateRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveTemplateAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("types")]
    public async Task<IActionResult> Types(CancellationToken ct)
    {
        var result = await service.GetDocumentTypesAsync(ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("types")]
    public async Task<IActionResult> CreateType([FromBody] SaveDocumentTypeDefinitionRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveDocumentTypeAsync(null, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPut("types/{id:long}")]
    public async Task<IActionResult> UpdateType(long id, [FromBody] SaveDocumentTypeDefinitionRequestDto request, CancellationToken ct)
    {
        var result = await service.SaveDocumentTypeAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}

public sealed class DeactivateDocumentRequest
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
