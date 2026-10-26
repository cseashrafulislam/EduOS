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

[ApiController]
[Route("api/finance/receivables-aging")]
[Authorize(Roles = "TenantAdmin,Principal,Accountant")]
[RequireModule("FINANCE")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeeReceivablesAgingController : ControllerBase
{
    private readonly IFeeReceivablesAgingService _service;
    public FeeReceivablesAgingController(IFeeReceivablesAgingService implementation) => _service = implementation;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] FeeReceivablesAgingQueryDto request, CancellationToken ct)
    {
        var result = await _service.GetReportAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
