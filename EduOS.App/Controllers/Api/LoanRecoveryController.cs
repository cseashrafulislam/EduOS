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

[ApiController, Route("api/finance/loan-recoveries")]
[Authorize(Roles = "TenantAdmin"), RequireModule("FINANCE"), RequireModule("PAYROLL")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoanRecoveryController : ControllerBase
{
    private readonly ILoanRecoveryService service;
    // Platform owns central DI; independently compose the institutional Finance service.
    public LoanRecoveryController(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
        => service = new LoanRecoveryService(db, user, clock);
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> result) =>
        StatusCode(result.StatusCode, result);

    [HttpGet]
    public async Task<IActionResult> Register([FromQuery] bool openOnly = true,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
        Reply(await service.GetRegisterAsync(openOnly, page, pageSize, ct));

    [HttpGet("{loanId:long}/history")]
    public async Task<IActionResult> History(long loanId, CancellationToken ct) =>
        Reply(await service.GetHistoryAsync(loanId, ct));

    [HttpPost("{loanId:long}/post")]
    public async Task<IActionResult> Post(long loanId,
        [FromBody] PostLoanRecoveryRequestDto request, CancellationToken ct) =>
        Reply(await service.PostAsync(loanId, request, ct));
}
