using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Text;

namespace EduOS.App.Controllers;

/// <summary>
/// Manager-only exports of immutable attendance correction events. Every request
/// re-runs the tenant/campus-scoped SQL query; no browser cache is authoritative.
/// </summary>
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
[RequireModule("ATTENDANCE")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("attendance/corrections")]
public sealed class AttendanceCorrectionExportsController(IStudentAttendanceService attendance) : Controller
{
    private const int ExportPageSize = 20;

    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] StudentAttendanceCorrectionReportQueryDto query,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await attendance.GetCorrectionReportAsync(
            query, page, ExportPageSize, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);

        var csv = new StringBuilder("Attendance date,Roll,Student code,Student,Subject offering,Previous,New,Reason,Changed at UTC,Changed by user\r\n");
        foreach (var item in result.Data.Items)
        {
            var cells = new string?[]
            {
                item.AttendanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.RollNo, item.StudentCode, item.StudentName,
                item.SubjectOfferingId?.ToString(CultureInfo.InvariantCulture) ?? "Batch",
                item.PreviousState.ToString(), item.NewState.ToString(), item.Reason,
                item.ChangedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                item.ChangedByUserId.ToString(CultureInfo.InvariantCulture)
            };
            csv.Append(string.Join(",", cells.Select(EscapeCsv))).Append("\r\n");
        }
        return File(Encoding.UTF8.GetBytes("\uFEFF" + csv),
            "text/csv; charset=utf-8",
            $"attendance-corrections-batch-{query.AcademicBatchId}-page-{page}.csv");
    }

    [HttpGet("print")]
    public async Task<IActionResult> Print(
        [FromQuery] StudentAttendanceCorrectionReportQueryDto query,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await attendance.GetCorrectionReportAsync(
            query, page, ExportPageSize, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);

        ViewData["BatchId"] = query.AcademicBatchId;
        ViewData["FromDate"] = query.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ViewData["ToDate"] = query.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ViewData["SubjectOffering"] = query.SubjectOfferingId?.ToString(CultureInfo.InvariantCulture) ?? "All";
        ViewData["StudentSearch"] = query.StudentSearch ?? "";
        ViewData["ChangedBy"] = query.ChangedByUserId?.ToString(CultureInfo.InvariantCulture) ?? "All";
        ViewData["Transition"] = $"{query.PreviousState?.ToString() ?? "All"} → {query.NewState?.ToString() ?? "All"}";
        return View("~/Views/Attendance/CorrectionAuditPrint.cshtml", result.Data);
    }

    private static string EscapeCsv(string? value)
    {
        var raw = value ?? string.Empty;
        var trimmed = raw.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]))
            raw = "'" + raw;
        return "\"" + raw.Replace("\"", "\"\"") + "\"";
    }
}
