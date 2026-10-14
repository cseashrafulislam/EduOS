using EduOS.App.Authorization;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/finance/fee-head-accounting")]
[Authorize(Roles = "TenantAdmin,Principal,Accountant"), RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeeHeadAccountingController : ControllerBase
{
    private readonly IFeeHeadAccountingService service;
    public FeeHeadAccountingController(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
        => service = new FeeHeadAccountingService(db, user, clock);
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> result) => StatusCode(result.StatusCode, result);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Reply(await service.GetSetupAsync(ct));

    [HttpPost("heads"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Create([FromBody] SaveFeeHeadAccountingHeadRequestDto request, CancellationToken ct)
        => Reply(await service.SaveHeadAsync(null, request, ct));

    [HttpPut("heads/{id:long}"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveFeeHeadAccountingHeadRequestDto request, CancellationToken ct)
        => Reply(await service.SaveHeadAsync(id, request, ct));

    [HttpPut("heads/{id:long}/accounts"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Map(long id, [FromBody] SaveFeeHeadAccountMappingRequestDto request, CancellationToken ct)
        => Reply(await service.MapAccountsAsync(id, request, ct));
}
