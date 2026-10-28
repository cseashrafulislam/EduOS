using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/finance/payroll-reconciliation")]
[Authorize(Roles = "TenantAdmin"), RequireModule("PAYROLL"), RequireModule("FINANCE")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PayrollReconciliationApiController : ControllerBase
{
    private readonly IPayrollReconciliationService service;
    public PayrollReconciliationApiController(IPayrollReconciliationService service) => this.service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int year, [FromQuery] int month,
        [FromQuery] long? campusId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (pageSize > 50) return BadRequest("Interactive page size cannot exceed 50.");
        var result = await service.GetAsync(year, month, campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("csv")]
    public async Task<IActionResult> Csv([FromQuery] int year, [FromQuery] int month,
        [FromQuery] long? campusId, CancellationToken ct = default)
    {
        var result = await service.GetAsync(year, month, campusId, 1, 250, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        var csv = new StringBuilder("\uFEFF");
        csv.AppendLine("Run,Year,Month,Campus ID,Employee Code,Employee Name,Net Payable BDT,Verified Paid BDT,Pending Verification BDT,Outstanding BDT,Accrual Status,Settlement Status");
        foreach (var row in result.Data.Items)
        {
            var values = new[] {
                row.RunNumber, row.Year.ToString(CultureInfo.InvariantCulture),
                row.Month.ToString(CultureInfo.InvariantCulture),
                row.CampusId?.ToString(CultureInfo.InvariantCulture) ?? "Institution-wide",
                row.EmployeeCode, row.EmployeeName,
                row.NetPayable.ToString("0.00",CultureInfo.InvariantCulture),
                row.VerifiedPaid.ToString("0.00",CultureInfo.InvariantCulture),
                row.PendingVerification.ToString("0.00",CultureInfo.InvariantCulture),
                row.Outstanding.ToString("0.00",CultureInfo.InvariantCulture),
                row.AccrualStatus, row.SettlementStatus
            };
            csv.AppendLine(string.Join(",", values.Select(SafeCsv)));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv",
            $"payroll-reconciliation-{year:D4}-{month:D2}.csv");
    }

    private static string SafeCsv(string? value)
    {
        var text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 0 && "=+-@\t".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
