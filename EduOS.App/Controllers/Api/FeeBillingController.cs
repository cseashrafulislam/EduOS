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

    [HttpGet("discount-rules")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> DiscountRules([FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetDiscountRulesAsync(page, pageSize, ct));

    [HttpPost("discount-rules")]
    [HttpPut("discount-rules/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> SaveDiscountRule(long? id,
        [FromBody] SaveDiscountRuleRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveDiscountRuleAsync(id, request, ct));

    [HttpGet("options")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Options(CancellationToken ct) =>
        Result(await _service.GetOptionsAsync(ct));

    [HttpGet("students/search")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> StudentOptions([FromQuery] string search,
        [FromQuery] int take = 20, CancellationToken ct = default) =>
        Result(await _service.SearchStudentsAsync(search, take, ct));

    [HttpGet("structures")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> StructureRegister([FromQuery] long? campusId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        Result(await _service.GetStructureRegisterAsync(campusId, page, pageSize, ct));

    [HttpGet("structures/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> StructureDetail(long id, CancellationToken ct) =>
        Result(await _service.GetStructureAsync(id, ct));

    [HttpPost("structures/{id:long}/close")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CloseStructure(long id,
        [FromBody] CloseFeeStructureRequestDto request, CancellationToken ct) =>
        Result(await _service.CloseFeeStructureAsync(id, request, ct));

    [HttpPost("structures")]
    [HttpPut("structures/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> SaveStructure(long? id,
        [FromBody] SaveFeeStructureRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveFeeStructureAsync(id, request, ct));

    [HttpPost("invoices/preflight")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> PreviewInvoices([FromBody] GenerateStudentInvoiceBatchRequestDto request,
        CancellationToken ct) =>
        Result(await _service.PreviewInvoiceBatchAsync(request, ct));

    [HttpPost("invoices/generate")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Generate([FromBody] GenerateStudentInvoiceBatchRequestDto request,
        CancellationToken ct) =>
        Result(await _service.GenerateInvoicesAsync(request, ct));

    [HttpPost("payments")]
    public async Task<IActionResult> Collect([FromBody] CreateStudentPaymentRequestDto request,
        CancellationToken ct) =>
        Result(await _service.CollectPaymentAsync(request, ct));

    [HttpGet("students/{reference:guid}/invoices/{documentReference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> InvoiceDocument(Guid reference, Guid documentReference, CancellationToken ct) =>
        Result(await _service.GetStudentInvoiceDocumentAsync(reference, documentReference, ct));

    [HttpGet("students/{reference:guid}/payments/{documentReference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> PaymentDocument(Guid reference, Guid documentReference, CancellationToken ct) =>
        Result(await _service.GetStudentPaymentDocumentAsync(reference, documentReference, ct));

    [HttpGet("students/{reference:guid}/invoices/{invoiceReference:guid}/allocation-audit")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> InvoiceAllocationAudit(Guid reference, Guid invoiceReference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetInvoiceAllocationAuditAsync(reference, invoiceReference, page, pageSize, ct));

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
