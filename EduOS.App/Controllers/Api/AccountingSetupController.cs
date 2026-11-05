using EduOS.App.Authorization;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace EduOS.App.Controllers.Api;
[ApiController, Route("api/finance/accounting/setup"), Authorize(Roles="TenantAdmin,Principal,Accountant"), RequireModule("FINANCE"), AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
public sealed class AccountingSetupController : ControllerBase
{
    private readonly IAccountingSetupService service;
    public AccountingSetupController(IAccountingSetupService implementation) => service = implementation;
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> r) => StatusCode(r.StatusCode,r);
    [HttpGet("accounts")] public async Task<IActionResult> Accounts(int page=1,int pageSize=25,string? search=null,CancellationToken ct=default) => Reply(await service.GetAccountsAsync(page,pageSize,search,ct));
    [HttpGet("accounts/{id:long}")] public async Task<IActionResult> Account(long id,CancellationToken ct) => Reply(await service.GetAccountAsync(id,ct));
    [HttpPost("accounts"),Authorize(Roles="TenantAdmin,Principal")] public async Task<IActionResult> CreateAccount([FromBody] SaveAccountRequestDto r,CancellationToken ct) => Reply(await service.SaveAccountAsync(null,r,ct));
    [HttpPut("accounts/{id:long}"),Authorize(Roles="TenantAdmin,Principal")] public async Task<IActionResult> UpdateAccount(long id,[FromBody] SaveAccountRequestDto r,CancellationToken ct) => Reply(await service.SaveAccountAsync(id,r,ct));
    [HttpGet("periods")] public async Task<IActionResult> Periods(int? year,CancellationToken ct) => Reply(await service.GetPeriodsAsync(year,ct));
    [HttpPost("periods"),Authorize(Roles="TenantAdmin,Principal")] public async Task<IActionResult> CreatePeriod([FromBody] SaveAccountingPeriodRequestDto r,CancellationToken ct) => Reply(await service.SavePeriodAsync(null,r,ct));
    [HttpPut("periods/{id:long}"),Authorize(Roles="TenantAdmin,Principal")] public async Task<IActionResult> UpdatePeriod(long id,[FromBody] SaveAccountingPeriodRequestDto r,CancellationToken ct) => Reply(await service.SavePeriodAsync(id,r,ct));
    [HttpGet("periods/{id:long}/close-readiness"),Authorize(Roles="TenantAdmin,Principal"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> PeriodCloseReadiness(long id,CancellationToken ct) => Reply(await service.GetPeriodCloseReadinessAsync(id,ct));
    [HttpPost("periods/{id:long}/close"),Authorize(Roles="TenantAdmin,Principal")] public async Task<IActionResult> ClosePeriod(long id,[FromBody] CloseAccountingPeriodRequestDto r,CancellationToken ct) => Reply(await service.ClosePeriodAsync(id,r,ct));
    [HttpGet("banks"),Authorize(Roles="TenantAdmin,Principal")] public async Task<IActionResult> Banks(CancellationToken ct) => Reply(await service.GetBankAccountsAsync(ct));
    [HttpPost("banks"),Authorize(Roles="TenantAdmin,Principal")] public async Task<IActionResult> CreateBank([FromBody] SaveBankAccountRequestDto r,CancellationToken ct) => Reply(await service.SaveBankAccountAsync(null,r,ct));
    [HttpPut("banks/{id:long}"),Authorize(Roles="TenantAdmin,Principal")] public async Task<IActionResult> UpdateBank(long id,[FromBody] SaveBankAccountRequestDto r,CancellationToken ct) => Reply(await service.SaveBankAccountAsync(id,r,ct));
    [HttpGet("cash-designations"),Authorize(Roles="TenantAdmin,Principal"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> CashDesignations(int page=1,int pageSize=25,CancellationToken ct=default) => Reply(await service.GetCashDesignationRequestsAsync(page,pageSize,ct));
    [HttpPost("cash-designations"),Authorize(Roles="TenantAdmin,Principal")]
    public async Task<IActionResult> SubmitCashDesignation([FromBody] SubmitCashLedgerDesignationDto r,CancellationToken ct) => Reply(await service.SubmitHistoricCashDesignationAsync(r,ct));
    [HttpPost("cash-designations/{id:long}/review"),Authorize(Roles="TenantAdmin")]
    public async Task<IActionResult> ReviewCashDesignation(long id,[FromBody] ReviewCashLedgerDesignationDto r,CancellationToken ct) => Reply(await service.ReviewHistoricCashDesignationAsync(id,r,ct));
    [HttpGet("cash-flow-rules"),Authorize(Roles="TenantAdmin,Principal"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> CashFlowRules(int page=1,int pageSize=25,CancellationToken ct=default) => Reply(await service.GetCashFlowRulesAsync(page,pageSize,ct));
    [HttpPost("cash-flow-rules"),Authorize(Roles="TenantAdmin,Principal")]
    public async Task<IActionResult> SubmitCashFlowRule([FromBody] SubmitCashFlowRuleDto r,CancellationToken ct) => Reply(await service.SubmitCashFlowRuleAsync(r,ct));
    [HttpPost("cash-flow-rules/{id:long}/review"),Authorize(Roles="TenantAdmin")]
    public async Task<IActionResult> ReviewCashFlowRule(long id,[FromBody] ReviewCashFlowRuleDto r,CancellationToken ct) => Reply(await service.ReviewCashFlowRuleAsync(id,r,ct));
}
