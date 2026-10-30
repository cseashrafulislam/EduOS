using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
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

    /// <summary>
    /// Empty, version-stable XLSX template. The example is on a separate worksheet,
    /// so it can never be accidentally imported as a real student.
    /// </summary>
    [HttpGet("template")]
    public IActionResult DownloadTemplate()
    {
        using var book = new XLWorkbook();
        var data = book.Worksheets.Add("Students");
        for (var i = 0; i < StudentImportColumns.All.Length; i++)
        {
            var cell = data.Cell(1, i + 1);
            cell.Value = StudentImportColumns.All[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = i < StudentImportColumns.Required.Length
                ? XLColor.LightBlue : XLColor.LightGreen;
        }
        data.SheetView.FreezeRows(1);
        data.Range(1, 1, 1, StudentImportColumns.All.Length).SetAutoFilter();
        data.Columns(1, StudentImportColumns.All.Length).Width = 19;
        // Keep student codes, rolls, phone numbers and date strings literal;
        // Excel's General format silently destroys leading zeroes.
        data.Columns(1, StudentImportColumns.All.Length).Style.NumberFormat.Format = "@";
        data.Column(3).Width = 30;

        var guide = book.Worksheets.Add("Guide");
        guide.Cell(1, 1).Value = "Student Import: fixed columns";
        guide.Cell(1, 1).Style.Font.Bold = true;
        guide.Cell(2, 1).Value = "Enter real students on the Students worksheet. Keep row 1 unchanged.";
        guide.Cell(3, 1).Value = "Blue headers are required; green headers are optional.";
        guide.Cell(4, 1).Value = "CampusId and AcademicBatchId must already exist in your institution.";
        guide.Cell(5, 1).Value = "SourceKey must remain unique and stable when retrying an import.";
        guide.Cell(6, 1).Value = "DateOfBirth format: yyyy-MM-dd. Do not use Excel formulas.";
        guide.Cell(7, 1).Value = "Import size is based on uploaded data; processing uses recoverable batches.";
        guide.Cell(8, 1).Value = "Cells are text-formatted to preserve leading zeroes in codes, roll numbers and phone numbers.";
        guide.Column(1).Width = 95;

        var example = book.Worksheets.Add("ExampleOnly");
        var sample = new[]
        {
            "SOURCE-0001", "STUDENT-0001", "Example Student", "YOUR_CAMPUS_ID",
            "YOUR_ACADEMIC_BATCH_ID", "001", "2010-01-01", "Example Guardian",
            "01700000000", "GUARDIAN-0001"
        };
        for (var i = 0; i < StudentImportColumns.All.Length; i++)
        {
            example.Cell(1, i + 1).Value = StudentImportColumns.All[i];
            example.Cell(1, i + 1).Style.Font.Bold = true;
            example.Cell(2, i + 1).Value = sample[i];
        }
        example.Columns(1, sample.Length).Width = 23;
        example.Columns(1, sample.Length).Style.NumberFormat.Format = "@";
        example.Column(3).Width = 30;

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "eduos-student-import-template.xlsx");
    }

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

    /// <summary>Reconcile selected independently committed source files without learner PII.</summary>
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
        if (files == null || files.Count < 2 ||
            files.Any(file => ValidateFile(file) != null) ||
            files.Sum(file => file.Length) > 8L * MaxFileSize)
            return BadRequest("Select two or more CSV/XLSX files, each at most 6 MB (48 MB per request).");
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

    /// <summary>
    /// Reauthorize the selected source cohort and regenerate the CSV from fresh
    /// tenant-owned SQL state. Never export a cached browser reconciliation.
    /// </summary>
    [HttpPost("cohort-status/export")]
    public async Task<IActionResult> ExportCohortStatus(
        [FromBody] List<StudentImportCohortFileRequestDto>? files,
        CancellationToken cancellationToken = default)
    {
        var result = await _execution.GetCohortStatusAsync(files, cancellationToken);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        var report = result.Data;
        if (report.ExpectedRows < 1 || report.Files.Count < 1)
            return StatusCode(409, "Cohort reconciliation exceeds export limits.");

        var csv = new StringBuilder(
            "FileHash,MappingHash,ExpectedRows,CommittedRows,LogItemsVerifiedRows,LogItemReviewWindows,MissingWindows,Status\r\n");
        foreach (var file in report.Files)
            csv.Append(SafeCsvCell(file.FileHash)).Append(',')
                .Append(SafeCsvCell(file.MappingHash)).Append(',')
                .Append(file.TotalRows).Append(',').Append(file.CommittedRows).Append(',')
                .Append(file.LogItemsVerifiedRows).Append(',').Append(file.LogItemReviewWindows)
                .Append(',').Append(file.MissingWindows).Append(',')
                .Append(SafeCsvCell(file.IsLogItemReconciled ? "LogItemsVerified" :
                    file.IsComplete ? "Review" : "Pending")).Append("\r\n");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", "student-import-cohort-summary.csv");
    }

    /// <summary>
    /// Recheck role, tenant and the exact committed import reference on every
    /// download. Issues are row numbers and controlled audit codes, never PII.
    /// </summary>
    [HttpGet("{reference:guid}/integrity/export")]
    public async Task<IActionResult> ExportIntegrity(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _execution.GetIntegrityAsync(reference, cancellationToken);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        var report = result.Data;
        if (report.ExpectedRows is < 1 or > 100 || report.Issues.Count > 2000)
            return StatusCode(409, "Import integrity report exceeds export limits.");

        var csv = new StringBuilder("RowNumber,IssueCode,Details\r\n");
        foreach (var issue in report.Issues)
            csv.Append(issue.RowNumber).Append(',')
                .Append(SafeCsvCell(issue.Code)).Append(',')
                .Append(SafeCsvCell(issue.Message)).Append("\r\n");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", "student-import-integrity.csv");
    }

    /// <summary>Read-only tenant-owned import integrity audit, without learner PII.</summary>
    [HttpGet("{reference:guid}/integrity")]
    public async Task<IActionResult> Integrity(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _execution.GetIntegrityAsync(reference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Bounded, PII-free preview. No enrollment or result is modified.</summary>
    [HttpGet("{reference:guid}/academic-repair")]
    public async Task<IActionResult> PreviewAcademicRepair(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _execution.PreviewAcademicRepairAsync(reference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Explicit tenant-admin reconciliation of an unchanged import window.
    /// Uses the preview fingerprint, antiforgery token and idempotent request ID.
    /// </summary>
    [HttpPost("{reference:guid}/academic-repair")]
    public async Task<IActionResult> ApplyAcademicRepair(Guid reference,
        [FromForm] Guid clientRequestId, [FromForm] string? expectedPlanHash,
        CancellationToken cancellationToken)
    {
        var result = await _execution.ApplyAcademicRepairAsync(reference,
            clientRequestId, expectedPlanHash ?? "", cancellationToken);
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
