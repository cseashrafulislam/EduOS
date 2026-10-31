using EduOS.App.Authorization;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/procurement/supplier-invoices"), Authorize,
 RequireModule("INVENTORY"), AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementSupplierInvoiceMatchingController(IInventoryMovementService inventory) : ControllerBase
{
    [HttpPost("preview"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Preview([FromBody] PreviewProcurementInvoiceRequest request, CancellationToken ct)
    {
        var result = await inventory.PreviewSupplierInvoiceMatchAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
