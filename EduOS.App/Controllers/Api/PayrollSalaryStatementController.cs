using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/payroll-salary-statement")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("PAYROLL")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PayrollSalaryStatementController : ControllerBase
{
    private readonly IPayrollSalaryStatementService _service;
    public PayrollSalaryStatementController(IPayrollSalaryStatementService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? year, [FromQuery] int? month,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        if (pageSize > 50) return BadRequest("Maximum interactive page size is 50.");
        var result = await _service.GetAsync(year, month, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("csv")]
    public async Task<IActionResult> Export([FromQuery] int year, [FromQuery] int month,
        CancellationToken ct = default)
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            return BadRequest("Valid year and month are required for an export.");
        var result = await _service.GetAsync(year, month, 1, 1000, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        var sb = new StringBuilder("\uFEFF");
        sb.AppendLine("Run,Period Start,Period End,Status,Employee Code,Employee Name,Gross BDT,Deductions BDT,Net BDT");
        foreach (var row in result.Data.Items)
        {
            var cols = new[] {
                row.RunNumber, row.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.State.ToString(), row.EmployeeCode, row.EmployeeName,
                row.GrossAmount.ToString("0.00", CultureInfo.InvariantCulture),
                row.DeductionAmount.ToString("0.00", CultureInfo.InvariantCulture),
                row.NetAmount.ToString("0.00", CultureInfo.InvariantCulture)
            };
            sb.AppendLine(string.Join(",", cols.Select(Csv)));
        }
        // Byte-order mark preserves Bangla names when opened by spreadsheet programs.
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
            $"payroll-salary-statement-{year:D4}-{month:D2}.csv");
    }

    private static string Csv(string? value)
    {
        var safe = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        // Prevent formula execution when exported names or codes are opened in Excel.
        if (safe.Length > 0 && "=+-@\t".Contains(safe[0])) safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
