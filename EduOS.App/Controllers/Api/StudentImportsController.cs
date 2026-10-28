using System.Text;
using System.Text.Json;
using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
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
        if (mapping == null || mapping.Count is < 6 or > 10)
            return BadRequest("Required import column mapping is missing.");
        await using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        var result = await _service.PreviewAsync(stream, Path.GetExtension(file.FileName), mapping, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Tenant-scoped, bounded full validation error CSV. No data is committed.</summary>
    [HttpPost("errors")]
    public async Task<IActionResult> ExportErrors([FromForm] IFormFile? file,
        [FromForm] string? mappingJson, CancellationToken cancellationToken)
    {
        var invalid = ValidateFile(file);
        if (invalid != null) return invalid;
        Dictionary<string, string>? mapping;
        try { mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson ?? ""); }
        catch (JsonException) { return BadRequest("Invalid column mapping."); }
        if (mapping == null || mapping.Count is < 6 or > 10)
            return BadRequest("Required import column mapping is missing.");
        await using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        var result = await _service.PreviewAllErrorsAsync(stream, Path.GetExtension(file.FileName),
            mapping, cancellationToken);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        var csv = new StringBuilder("RowNumber,SourceKey,StudentCode,Issues\r\n");
        foreach (var row in result.Data.Errors)
            csv.Append(row.RowNumber).Append(',')
                .Append(SafeCsvCell(row.SourceKey)).Append(',')
                .Append(SafeCsvCell(row.StudentCode)).Append(',')
                .Append(SafeCsvCell(string.Join("; ", row.Issues))).Append("\r\n");
        // UTF-8 BOM for Excel; neutralize spreadsheet formulas in all user-controlled cells.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "student-import-errors.csv");
    }

    private static string SafeCsvCell(string? input)
    {
        var value = input ?? string.Empty;
        var i = 0;
        while (i < value.Length && (char.IsWhiteSpace(value[i]) || char.IsControl(value[i]))) i++;
        if (i < value.Length && "=+-@".Contains(value[i])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromForm] IFormFile? file,
        [FromForm] string? mappingJson, [FromForm] Guid clientRequestId,
        [FromForm] int? rowOffset, CancellationToken cancellationToken)
    {
        var invalid = ValidateFile(file);
        if (invalid != null) return invalid;
        Dictionary<string, string>? mapping;
        try { mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson ?? ""); }
        catch (JsonException) { return BadRequest("Invalid column mapping."); }
        if (mapping == null || mapping.Count is < 6 or > 10 || clientRequestId == Guid.Empty)
            return BadRequest("Valid column mapping and import request ID are required.");
        await using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        var result = rowOffset.HasValue
            ? await _execution.ExecuteSliceAsync(stream, Path.GetExtension(file.FileName),
                mapping, clientRequestId, rowOffset.Value, cancellationToken)
            : await _execution.ExecuteAsync(stream, Path.GetExtension(file.FileName),
                mapping, clientRequestId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Tenant-authorized export of uncommitted source rows for correction.</summary>
    [HttpPost("pending-corrections")]
    public async Task<IActionResult> PendingCorrections([FromForm] IFormFile? file,
        [FromForm] string? mappingJson, CancellationToken cancellationToken)
    {
        var invalid = ValidateFile(file);
        if (invalid != null) return invalid;
        Dictionary<string, string>? mapping;
        try { mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson ?? ""); }
        catch (JsonException) { return BadRequest("Invalid column mapping."); }
        if (mapping == null || mapping.Count is < 6 or > 10)
            return BadRequest("Required import column mapping is missing.");
        await using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        var result = await _execution.ExportPendingCorrectionsAsync(stream,
            Path.GetExtension(file.FileName), mapping, mappingJson!, cancellationToken);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        return File(result.Data,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "student-import-pending-corrections.xlsx");
    }

    [HttpGet("job-status")]
    public async Task<IActionResult> JobStatus([FromQuery] string fileHash,
        [FromQuery] string mappingHash, [FromQuery] int totalRows,
        CancellationToken cancellationToken = default)
    {
        var result = await _execution.GetJobStatusAsync(fileHash, mappingHash, totalRows, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Reconcile up to eight independently committed source files without learner PII.</summary>
    [HttpPost("cohort-status")]
    public async Task<IActionResult> CohortStatus(
        [FromBody] List<StudentImportCohortFileRequestDto>? files,
        CancellationToken cancellationToken = default)
    {
        var result = await _execution.GetCohortStatusAsync(files, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Read-only, bounded cross-file duplicate and guardian-profile preflight.</summary>
    [HttpPost("cohort-preflight")]
    [RequestSizeLimit(52 * 1024 * 1024)]
    public async Task<IActionResult> CohortPreflight(
        [FromForm] List<IFormFile>? files, [FromForm] string? mappingJson,
        [FromQuery] bool export = false, CancellationToken cancellationToken = default)
    {
        if (files == null || files.Count is < 2 or > 8 ||
            files.Any(file => ValidateFile(file) != null) ||
            files.Sum(file => file.Length) > 8L * MaxFileSize)
            return BadRequest("Select two to eight CSV/XLSX files, each at most 6 MB (48 MB total).");
        Dictionary<string, string>? mapping;
        try { mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson ?? ""); }
        catch (JsonException) { return BadRequest("Invalid column mapping."); }
        if (mapping == null || mapping.Count is < 6 or > 10)
            return BadRequest("Required column mapping is missing.");

        var sources = new List<StudentImportCohortUploadDto>(files.Count);
        foreach (var file in files)
        {
            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            sources.Add(new StudentImportCohortUploadDto
            {
                Content = buffer.ToArray(), Extension = Path.GetExtension(file.FileName)
            });
        }
        var result = await _execution.PreflightCohortAsync(sources, mapping, cancellationToken);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        if (export)
        {
            var csv = new StringBuilder("FileNumber,RowNumber,Field,Issue\r\n");
            foreach (var issue in result.Data.Issues)
                csv.Append(issue.FileNumber).Append(',').Append(issue.RowNumber).Append(',')
                    .Append(SafeCsvCell(issue.Field)).Append(',')
                    .Append(SafeCsvCell(issue.Message)).Append("\r\n");
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            if (bytes.Length > 16 * 1024 * 1024)
                return StatusCode(413, "Conflict export exceeds 16 MB.");
            return File(bytes, "text/csv; charset=utf-8", "student-import-cohort-conflicts.csv");
        }
        var report = result.Data;
        return Ok(EduOS.Core.Common.ApiResponse<StudentImportCohortPreflightDto>.SuccessResponse(
            new StudentImportCohortPreflightDto
            {
                FileCount = report.FileCount, TotalRows = report.TotalRows,
                TotalIssues = report.TotalIssues, Issues = report.Issues.Take(100).ToList()
            }));
    }

    /// <summary>Read-only tenant-owned import integrity audit, without learner PII.</summary>
    [HttpGet("{reference:guid}/integrity")]
    public async Task<IActionResult> Integrity(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _execution.GetIntegrityAsync(reference, cancellationToken);
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
