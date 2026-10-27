using System.Text.Json;
using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-imports")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(8 * 1024 * 1024)]
public sealed class StudentImportsController : ControllerBase
{
    private const long MaxFileSize = 6 * 1024 * 1024;
    private readonly IStudentImportPreviewService _service;
    private readonly IStudentImportExecutionService _execution;

    public StudentImportsController(IStudentImportPreviewService service, IStudentImportExecutionService execution)
    { _service = service; _execution = execution; }

    [HttpPost("headers")]
    public async Task<IActionResult> Headers([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        var invalid = ValidateFile(file);
        if (invalid != null) return invalid;
        await using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        var result = await _service.GetHeadersAsync(stream, Path.GetExtension(file.FileName), cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromForm] IFormFile? file,
        [FromForm] string? mappingJson, CancellationToken cancellationToken)
    {
        var invalid = ValidateFile(file);
        if (invalid != null) return invalid;
        Dictionary<string, string>? mapping;
        try
        {
            mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson ?? "");
        }
        catch (JsonException) { return BadRequest("Invalid column mapping."); }
        if (mapping == null || mapping.Count is < 6 or > 9)
            return BadRequest("Required import column mapping is missing.");
        await using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        var result = await _service.PreviewAsync(stream, Path.GetExtension(file.FileName), mapping, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromForm] IFormFile? file,
        [FromForm] string? mappingJson, [FromForm] Guid clientRequestId, CancellationToken cancellationToken)
    {
        var invalid = ValidateFile(file);
        if (invalid != null) return invalid;
        Dictionary<string, string>? mapping;
        try { mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson ?? ""); }
        catch (JsonException) { return BadRequest("Invalid column mapping."); }
        if (mapping == null || mapping.Count is < 6 or > 9 || clientRequestId == Guid.Empty)
            return BadRequest("Valid column mapping and import request ID are required.");
        await using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        var result = await _execution.ExecuteAsync(stream, Path.GetExtension(file.FileName),
            mapping, clientRequestId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Reconciliation(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _execution.GetAsync(reference, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    private IActionResult? ValidateFile(IFormFile? file)
    {
        if (file == null || file.Length <= 0)
            return BadRequest("Choose a non-empty CSV or XLSX file.");
        if (file.Length > MaxFileSize)
            return StatusCode(413, "File exceeds the 6 MB preflight limit.");
        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only CSV or XLSX files are accepted.");
        return null;
    }
}
