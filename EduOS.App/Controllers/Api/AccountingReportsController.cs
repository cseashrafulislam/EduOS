using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/finance/accounting/reports"), Authorize(Roles = "TenantAdmin,Principal,Accountant"),
 RequireModule("FINANCE"), EnableRateLimiting("ApiPolicy")]
public sealed class AccountingReportsController : ControllerBase
{
    private readonly IAccountingReportingService service;
    public AccountingReportsController(EduOSDbContext db, ICurrentUserService user)
        => service = new AccountingReportingService(db, user);
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);
    [HttpGet("ledger/{accountId:long}")]
    public async Task<IActionResult> Ledger(long accountId, [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, CancellationToken ct)
        => Reply(await service.GetLedgerStatementAsync(accountId, fromDate, toDate, ct));
    [HttpGet("income-statement"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> IncomeStatement([FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, CancellationToken ct)
        => Reply(await service.GetIncomeStatementAsync(fromDate, toDate, ct));
    [HttpGet("balance-sheet"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> BalanceSheet([FromQuery] DateOnly asOfDate, CancellationToken ct)
        => Reply(await service.GetBalanceSheetAsync(asOfDate, ct));
    [HttpGet("bank-liquidity"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> BankLiquidity([FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, CancellationToken ct)
        => Reply(await service.GetBankLiquidityAsync(fromDate, toDate, ct));
    [HttpGet("bank-book/{bankAccountId:long}"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> BankBook(long bankAccountId, [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, CancellationToken ct)
        => Reply(await service.GetBankBookAsync(bankAccountId, fromDate, toDate, ct));
    [HttpGet("trial-balance")]
    public async Task<IActionResult> TrialBalance([FromQuery] DateOnly asOfDate, CancellationToken ct)
        => Reply(await service.GetTrialBalanceAsync(asOfDate, ct));
}
