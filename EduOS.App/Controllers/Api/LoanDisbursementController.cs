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

[ApiController, Route("api/finance/loan-disbursements")]
[Authorize(Roles = "TenantAdmin"), RequireModule("FINANCE"), RequireModule("PAYROLL")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoanDisbursementController : ControllerBase
{
    private readonly ILoanDisbursementService service;
    public LoanDisbursementController(ILoanDisbursementService implementation) => service = implementation;
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> response) =>
        StatusCode(response.StatusCode, response);
    [HttpGet]
    public async Task<IActionResult> Register([FromQuery] bool pendingOnly = true,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
        Reply(await service.GetRegisterAsync(pendingOnly, page, pageSize, ct));
    [HttpPost("{loanId:long}/post")]
    public async Task<IActionResult> Post(long loanId,
        [FromBody] PostLoanDisbursementRequestDto request, CancellationToken ct) =>
        Reply(await service.PostAsync(loanId, request, ct));
}
