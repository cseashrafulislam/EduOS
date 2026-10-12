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
    [HttpGet("trial-balance")]
    public async Task<IActionResult> TrialBalance([FromQuery] DateOnly asOfDate, CancellationToken ct)
        => Reply(await service.GetTrialBalanceAsync(asOfDate, ct));
}
