using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal,Accountant,Cashier")]
[RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("api/finance/fees")]
public sealed class FeeBillingController : ControllerBase
{
    private readonly IFeeBillingService _service;
    public FeeBillingController(IFeeBillingService service) => _service = service;

    [HttpPut("structure")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> SaveStructure([FromBody] SaveFeeStructureDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var result = await _service.SaveFeeStructureAsync(request, cancellationToken); return StatusCode(result.StatusCode, result); }
        catch (DbUpdateConcurrencyException) { var result = ApiResponse<bool>.ErrorResponse("Fee structure changed by another user. Reload and try again.", 409); return Conflict(result); }
        catch (DbUpdateException) { var result = ApiResponse<bool>.ErrorResponse("Fee structure conflicts with an existing billing configuration. Reload and try again.", 409); return Conflict(result); }
    }

    [HttpPost("invoices/generate")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Generate([FromBody] GenerateStudentInvoicesDto request, CancellationToken cancellationToken) { if (!ModelState.IsValid) return ValidationProblem(ModelState); var result = await _service.GenerateInvoicesAsync(request, cancellationToken); return StatusCode(result.StatusCode, result); }

    [HttpPost("payments")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Collect([FromBody] CollectStudentPaymentDto request, CancellationToken cancellationToken) { if (!ModelState.IsValid) return ValidationProblem(ModelState); var result = await _service.CollectPaymentAsync(request, cancellationToken); return StatusCode(result.StatusCode, result); }

    [HttpPut("invoices/fine")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> SetFine([FromBody] SetInvoiceFineDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var result = await _service.SetFineAsync(request, cancellationToken); return StatusCode(result.StatusCode, result); }
        catch (DbUpdateConcurrencyException) { var result = ApiResponse<StudentInvoiceDto>.ErrorResponse("Invoice changed by another user. Reload and try again.", 409); return Conflict(result); }
    }

    [HttpGet("students/{studentReference:guid}/ledger")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Ledger(Guid studentReference, CancellationToken cancellationToken) { var result = await _service.GetStudentLedgerAsync(studentReference, cancellationToken); return StatusCode(result.StatusCode, result); }
}
