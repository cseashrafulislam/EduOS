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

[ApiController, Route("api/finance/payroll-posting")]
[Authorize(Roles = "TenantAdmin,Principal,Accountant"), RequireModule("FINANCE"), RequireModule("PAYROLL")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PayrollJournalPostingController : ControllerBase
{
    private readonly IPayrollJournalPostingService service;
    public PayrollJournalPostingController(IPayrollJournalPostingService implementation) => service = implementation;
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
    [HttpGet]
    public async Task<IActionResult> Register([FromQuery] bool unpostedOnly = true,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 5, CancellationToken ct = default) =>
        Reply(await service.GetRegisterAsync(unpostedOnly, page, pageSize, ct));
    [HttpPost("{reference:guid}/post"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Post(Guid reference,
        [FromBody] PostPayrollJournalRequestDto request, CancellationToken ct) =>
        Reply(await service.PostAsync(reference, request, ct));
}
