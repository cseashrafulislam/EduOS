using EduOS.App.Authorization;
using EduOS.App.Reporting;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers;

// Every export and print re-runs the authorized, bounded tenant/campus query.
// Browser-rendered audit data is never an authorization source.
[Authorize(Roles = "TenantAdmin,Principal,Accountant")]
[RequireModule("FINANCE")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("finance/fees/plans/{reference:guid}/billing-audit")]
public sealed class FeeInstallmentAuditExportsController(IFeeBillingService billing) : Controller
{
    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv(Guid reference, CancellationToken ct = default)
    {
        var result = await billing.GetInstallmentBillingAuditAsync(reference, ct);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);
        return File(Encoding.UTF8.GetBytes(FeeInstallmentBillingAuditCsv.Build(result.Data)),
            "text/csv; charset=utf-8",
            $"fee-installment-{reference:N}-billing-audit.csv");
    }

    [HttpGet("print")]
    public async Task<IActionResult> Print(Guid reference, CancellationToken ct = default)
    {
        var result = await billing.GetInstallmentBillingAuditAsync(reference, ct);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);
        return View("~/Views/FeeInstallmentPlans/BillingAuditPrint.cshtml", result.Data);
    }
}
