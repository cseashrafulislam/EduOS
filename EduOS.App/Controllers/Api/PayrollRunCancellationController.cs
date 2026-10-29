using EduOS.App.Authorization;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/hr-payroll/runs")]
[Authorize(Roles = "TenantAdmin"), RequireModule("PAYROLL")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PayrollRunCancellationController : ControllerBase
{
    private readonly IPayrollRunCancellationService service;
    public PayrollRunCancellationController(IPayrollRunCancellationService service) => this.service = service;

    [HttpPost("{reference:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid reference, [FromBody] CancelPayrollRunRequestDto request,
        CancellationToken ct)
    {
        var response = await service.CancelAsync(reference, request, ct);
        return StatusCode(response.StatusCode, response);
    }
}
