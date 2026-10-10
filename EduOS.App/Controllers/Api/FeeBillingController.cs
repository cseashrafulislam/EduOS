using EduOS.App.Authorization;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal,Accountant,Cashier")]
[RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/finance/fees")]
public sealed class FeeBillingController : ControllerBase
{
    private readonly IFeeBillingService _service;
    public FeeBillingController(IFeeBillingService service) => _service = service;

    [HttpGet("options")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Options(CancellationToken ct) =>
        Result(await _service.GetOptionsAsync(ct));

    [HttpGet("students/search")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> StudentOptions([FromQuery] string search,
        [FromQuery] int take = 20, CancellationToken ct = default) =>
        Result(await _service.SearchStudentsAsync(search, take, ct));

    [HttpPost("structures")]
    [HttpPut("structures/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> SaveStructure(long? id,
        [FromBody] SaveFeeStructureRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveFeeStructureAsync(id, request, ct));

    [HttpPost("invoices/generate")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Generate([FromBody] GenerateStudentInvoiceBatchRequestDto request,
        CancellationToken ct) =>
        Result(await _service.GenerateInvoicesAsync(request, ct));

    [HttpPost("payments")]
    public async Task<IActionResult> Collect([FromBody] CreateStudentPaymentRequestDto request,
        CancellationToken ct) =>
        Result(await _service.CollectPaymentAsync(request, ct));

    [HttpGet("students/{reference:guid}/ledger")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Ledger(Guid reference, CancellationToken ct) =>
        Result(await _service.GetStudentLedgerAsync(reference, ct));

    [HttpGet("students/{reference:guid}/invoices")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Invoices(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetStudentInvoicesAsync(reference, page, pageSize, ct));

    [HttpGet("students/{reference:guid}/payments")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Payments(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetStudentPaymentsAsync(reference, page, pageSize, ct));

    private IActionResult Result<T>(EduOS.Core.Common.ApiResponse<T> result) =>
        StatusCode(result.StatusCode, result);
}
