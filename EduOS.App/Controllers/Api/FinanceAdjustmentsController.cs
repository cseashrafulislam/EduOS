using EduOS.App.Authorization;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/finance/adjustments")]
[Authorize(Roles="TenantAdmin")]
[RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class FinanceAdjustmentsController : ControllerBase
{
    private readonly FinanceAdjustmentService service;
    public FinanceAdjustmentsController(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
        => service=new FinanceAdjustmentService(db,user,clock);
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> result) => StatusCode(result.StatusCode,result);
    [HttpGet("discount-rules")]
    public async Task<IActionResult> DiscountRules(CancellationToken ct) => Reply(await service.GetDiscountRulesAsync(ct));
    [HttpPost("discount-rules")]
    public async Task<IActionResult> CreateDiscountRule([FromBody] SaveDiscountRuleRequestDto request,CancellationToken ct)
        => Reply(await service.SaveDiscountRuleAsync(null,request,ct));
    [HttpPut("discount-rules/{id:long}")]
    public async Task<IActionResult> UpdateDiscountRule(long id,[FromBody] SaveDiscountRuleRequestDto request,CancellationToken ct)
        => Reply(await service.SaveDiscountRuleAsync(id,request,ct));
    [HttpPost("discounts")]
    public async Task<IActionResult> AssignDiscount([FromBody] AssignStudentDiscountRequestDto request,CancellationToken ct)
        => Reply(await service.AssignDiscountAsync(request,ct));
    [HttpPost("discounts/{id:long}/deactivate")]
    public async Task<IActionResult> DeactivateDiscount(long id,
        [FromBody] DeactivateStudentDiscountRequestDto request, CancellationToken ct)
        => Reply(await service.DeactivateStudentDiscountAsync(id,request,ct));
    [HttpGet("fine-rules")]
    public async Task<IActionResult> FineRules(CancellationToken ct) => Reply(await service.GetFineRulesAsync(ct));
    [HttpPost("fine-rules")]
    public async Task<IActionResult> CreateFineRule([FromBody] SaveFineRuleRequestDto request,CancellationToken ct)
        => Reply(await service.SaveFineRuleAsync(null,request,ct));
    [HttpPut("fine-rules/{id:long}")]
    public async Task<IActionResult> UpdateFineRule(long id,[FromBody] SaveFineRuleRequestDto request,CancellationToken ct)
        => Reply(await service.SaveFineRuleAsync(id,request,ct));
    [HttpPost("fines")]
    public async Task<IActionResult> ApplyFine([FromBody] ApplyStudentFineRequestDto request,CancellationToken ct)
        => Reply(await service.ApplyFineAsync(request,ct));
    [HttpPost("fines/{id:long}/waive")]
    public async Task<IActionResult> WaiveFine(long id,[FromBody] WaiveStudentFineRequestDto request,CancellationToken ct)
        => Reply(await service.WaiveFineAsync(id,request,ct));
    [HttpGet("refunds")]
    public async Task<IActionResult> Refunds([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Reply(await service.GetRefundRegisterAsync(campusId,page,pageSize,ct));
    [HttpPost("refunds")]
    public async Task<IActionResult> RequestRefund([FromBody] CreateRefundRequestDto request,CancellationToken ct)
        => Reply(await service.RequestRefundAsync(request,ct));
    [HttpPost("refunds/{id:long}/review")]
    public async Task<IActionResult> ReviewRefund(long id,[FromBody] ReviewRefundRequestDto request,CancellationToken ct)
        => Reply(await service.ReviewRefundAsync(id,request,ct));
    [HttpPost("refunds/{id:long}/record-payout")]
    public async Task<IActionResult> RecordRefundPayout(long id,[FromBody] RecordRefundDisbursementRequestDto request,CancellationToken ct)
        => Reply(await service.RecordRefundDisbursementAsync(id,request,ct));
    [HttpGet("enrollments/search")]
    public async Task<IActionResult> SearchEnrollments([FromQuery] string search,CancellationToken ct)
        => Reply(await service.SearchEnrollmentsAsync(search,ct));
    [HttpGet("enrollments/{reference:guid}")]
    public async Task<IActionResult> Register(Guid reference,CancellationToken ct)
        => Reply(await service.GetStudentRegisterAsync(reference,ct));
}
