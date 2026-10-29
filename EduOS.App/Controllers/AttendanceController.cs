using EduOS.App.Authorization;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher")]
[RequireModule("ATTENDANCE")]
public sealed class AttendanceController(IStudentAttendanceReportService reportService) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    // Reauthorize on every download; never export cached browser data or an unbounded roster.
    [HttpGet("attendance/sessions/{sessionId:long}/export.csv")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    [EnableRateLimiting("ApiPolicy")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportFinalizedSessionCsv(
        long sessionId, [FromQuery] int page = 1,
        [FromQuery] string? search = null, [FromQuery] AttendanceState? state = null,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 50;
        var result = await reportService.GetFilteredFinalizedSessionRosterAsync(
            sessionId, search, state, page, pageSize, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);

        var roster = result.Data;
        var csv = new StringBuilder();
        csv.Append("Session ID,Date,Batch ID,Page,Roll,Student Code,Student Name,Status,Check In,Check Out,Remarks\r\n");
        foreach (var mark in roster.Students)
        {
            var cells = new string?[]
            {
                roster.AttendanceSessionId.ToString(CultureInfo.InvariantCulture),
                roster.AttendanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                roster.AcademicBatchId.ToString(CultureInfo.InvariantCulture),
                roster.Page.ToString(CultureInfo.InvariantCulture),
                mark.RollNo, mark.StudentCode, mark.StudentName, mark.State.ToString(),
                mark.CheckInTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                mark.CheckOutTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                mark.Remarks
            };
            csv.Append(string.Join(",", cells.Select(EscapeCsv))).Append("\r\n");
        }
        var bytes = Encoding.UTF8.GetBytes("\uFEFF" + csv);
        return File(bytes, "text/csv; charset=utf-8",
            $"attendance-session-{sessionId}-page-{page}.csv");
    }

    [HttpGet("attendance/sessions/{sessionId:long}/print")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    [EnableRateLimiting("ApiPolicy")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrintFinalizedSession(
        long sessionId, [FromQuery] int page = 1,
        [FromQuery] string? search = null, [FromQuery] AttendanceState? state = null,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 50;
        var result = await reportService.GetFilteredFinalizedSessionRosterAsync(
            sessionId, search, state, page, pageSize, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);
        return View("SessionPrint", result.Data);
    }


    // Session register exports are always freshly scoped by the existing SQL report service.
    [HttpGet("attendance/sessions/register/export.csv")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    [EnableRateLimiting("ApiPolicy")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportSessionRegisterCsv(
        [FromQuery] StudentAttendanceReportQueryDto query, [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        query.PageSize = 20;
        var result = await reportService.GetSessionRegisterAsync(query, status, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);
        var csv = new StringBuilder("Session ID,Date,Offering,Status,Marked,Present,Absent,Late,Leave,Excused\r\n");
        foreach (var session in result.Data.Items)
        {
            var cells = new string?[]
            {
                session.SessionId.ToString(CultureInfo.InvariantCulture),
                session.AttendanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                session.SubjectOfferingCode, session.IsFinalized ? "Finalized" : "Draft",
                session.Marked.ToString(CultureInfo.InvariantCulture),
                session.Present.ToString(CultureInfo.InvariantCulture),
                session.Absent.ToString(CultureInfo.InvariantCulture),
                session.Late.ToString(CultureInfo.InvariantCulture),
                session.Leave.ToString(CultureInfo.InvariantCulture),
                session.Excused.ToString(CultureInfo.InvariantCulture)
            };
            csv.Append(string.Join(",", cells.Select(EscapeCsv))).Append("\r\n");
        }
        return File(Encoding.UTF8.GetBytes("\uFEFF" + csv), "text/csv; charset=utf-8",
            $"attendance-session-register-{query.AcademicBatchId}-page-{query.Page}.csv");
    }

    [HttpGet("attendance/sessions/register/print")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    [EnableRateLimiting("ApiPolicy")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrintSessionRegister(
        [FromQuery] StudentAttendanceReportQueryDto query, [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        query.PageSize = 20;
        var result = await reportService.GetSessionRegisterAsync(query, status, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);
        ViewData["BatchId"] = query.AcademicBatchId;
        ViewData["FromDate"] = query.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ViewData["ToDate"] = query.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ViewData["StatusFilter"] = status ?? "all";
        return View("SessionRegisterPrint", result.Data);
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
