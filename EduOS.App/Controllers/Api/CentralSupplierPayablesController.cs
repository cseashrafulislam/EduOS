using EduOS.App.Authorization;
using EduOS.Service.Services.Facilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

/// <summary>
/// Tenant-wide Finance read of the existing combined supplier subledger.
/// Separate controller preserves the one-contract Accounting Reports API.
/// No shadow payable totals or second GL are created.
/// </summary>
[ApiController, Route("api/finance/accounting/reports"),
 Authorize(Roles = "TenantAdmin"), RequireModule("FINANCE"),
 EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CentralSupplierPayablesController(
    FacilityVendorServiceBillService supplierPayables) : ControllerBase
{
    [HttpGet("supplier-payables-aging")]
    public async Task<IActionResult> SupplierPayablesAging([FromQuery] DateOnly asOf,
        [FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await supplierPayables.SupplierAgingAsync(asOf, campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("supplier-ap-gl-reconciliation")]
    public async Task<IActionResult> SupplierApGlReconciliation([FromQuery] DateOnly asOf,
        [FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await supplierPayables.SupplierAgingWithGlAsync(asOf, campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
}
