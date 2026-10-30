using EduOS.App.Authorization;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/finance/payroll-payments")]
[Authorize(Roles = "TenantAdmin,Principal,Accountant"), RequireModule("FINANCE"), RequireModule("PAYROLL")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PayrollPaymentPostingController : ControllerBase
{
    private readonly IPayrollPaymentPostingService service;
    public PayrollPaymentPostingController(IPayrollPaymentPostingService implementation) => service = implementation;
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
    [HttpGet]
    public async Task<IActionResult> Register([FromQuery] bool pendingOnly = true,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] Guid? runReference = null, CancellationToken ct = default) =>
        Reply(await service.GetRegisterAsync(pendingOnly, page, pageSize, ct, runReference));
    [HttpGet("bank-review.csv"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> BankReview([FromQuery] Guid runReference, CancellationToken ct)
    {
        var result = await service.GetBankReviewCsvAsync(runReference, ct);
        if (!result.Success || result.Data == null) return Reply(result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(new System.Text.UTF8Encoding(true).GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(result.Data.Csv)).ToArray(),
            "text/csv; charset=utf-8", result.Data.FileName);
    }
    [HttpPost("{reference:guid}/post"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Post(Guid reference,
        [FromBody] PostPayrollPaymentJournalRequestDto request, CancellationToken ct) =>
        Reply(await service.PostAsync(reference, request, ct));

    [HttpPost("{reference:guid}/returns"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> RequestReturn(Guid reference,
        [FromBody] RequestPayrollPaymentReturnDto request, CancellationToken ct) =>
        Reply(await service.RequestReturnAsync(reference, request, ct));

    [HttpPost("{reference:guid}/returns/{returnReference:guid}/review"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ReviewReturn(Guid reference, Guid returnReference,
        [FromBody] ReviewPayrollPaymentReturnDto request, CancellationToken ct) =>
        Reply(await service.ReviewReturnAsync(reference, returnReference, request, ct));
}
