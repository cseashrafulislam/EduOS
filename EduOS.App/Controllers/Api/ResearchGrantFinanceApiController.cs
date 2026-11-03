using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.App.Security;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/research/grants"), Authorize, RequireModule("RESEARCH"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResearchGrantFinanceApiController(IResearchQualityExtensionService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> result)
    {
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        var tenant = User.FindFirstValue("TenantId");
        if (sid is not { Length: 48 } || !long.TryParse(tenant, out var id) || id <= 0)
            return Unauthorized(new { success = false, message = "Session unavailable." });
        Response.Headers["X-EduOS-Session-Reference"] = AuthSessionRegistry.ToPublicReference(sid);
        Response.Headers["X-EduOS-Tenant-Id"] = tenant;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("finance-accounts")]
    public async Task<IActionResult> FinanceAccounts(long schemeId, CancellationToken ct) =>
        Reply(await service.FinanceAccountsAsync(schemeId, ct));

    [HttpPost("finance-accounts")]
    public async Task<IActionResult> ConfigureFinanceAccount([FromBody] GrantFinanceAccountCreateDto input,
        CancellationToken ct) => ModelState.IsValid
            ? Reply(await service.ConfigureFinanceAccountAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpPost("{reference:guid}/finance/journals")]
    public async Task<IActionResult> LinkJournal(Guid reference, [FromBody] GrantJournalLinkCreateDto input,
        CancellationToken ct) => ModelState.IsValid
            ? Reply(await service.LinkJournalAsync(reference, input, ct)) : ValidationProblem(ModelState);

    [HttpGet("{reference:guid}/finance")]
    public async Task<IActionResult> FinanceReport(Guid reference, CancellationToken ct) =>
        Reply(await service.FinanceReportAsync(reference, ct));
}
