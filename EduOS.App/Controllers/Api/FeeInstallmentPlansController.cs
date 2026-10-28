using EduOS.App.Authorization;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/finance/fees/plans")]
[Authorize(Roles = "TenantAdmin,Principal,Accountant")]
[RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeeInstallmentPlansController : ControllerBase
{
    private readonly IFeeBillingService service;
    public FeeInstallmentPlansController(IFeeBillingService service) => this.service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long? campusId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await service.GetInstallmentPlansAsync(campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct)
    {
        var result = await service.GetInstallmentPlanAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/billing-audit")]
    public async Task<IActionResult> BillingAudit(Guid reference, CancellationToken ct)
    {
        var result = await service.GetInstallmentBillingAuditAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStudentFeeInstallmentPlanDto request, CancellationToken ct)
    {
        var result = await service.CreateInstallmentPlanAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/issue-month")]
    public async Task<IActionResult> IssueMonth(Guid reference,
        [FromBody] IssueStudentFeeInstallmentMonthDto request, CancellationToken ct)
    {
        var result = await service.IssueInstallmentMonthAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/submit")]
    public async Task<IActionResult> Submit(Guid reference,
        [FromBody] StudentFeeInstallmentTransitionDto request, CancellationToken ct)
    {
        var result = await service.TransitionInstallmentPlanAsync(reference, 2, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/approve")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Approve(Guid reference,
        [FromBody] StudentFeeInstallmentTransitionDto request, CancellationToken ct)
    {
        var result = await service.TransitionInstallmentPlanAsync(reference, 3, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/cancel")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Cancel(Guid reference,
        [FromBody] StudentFeeInstallmentTransitionDto request, CancellationToken ct)
    {
        var result = await service.TransitionInstallmentPlanAsync(reference, 4, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
