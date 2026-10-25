using EduOS.App.Authorization;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/admission-cash-receipts")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("ADMISSION")]
[RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdmissionCashReceiptsController(IAdmissionCashReceiptService service) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace([FromQuery] string? search, CancellationToken ct)
    {
        var result = await service.GetWorkspaceAsync(search, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Receipt(Guid reference, CancellationToken ct)
    {
        var result = await service.GetReceiptAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Collect([FromBody] CollectAdmissionCashReceiptRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.CollectAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
