using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.App.Security;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Service.Services.Facilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

/// <summary>Management-only allocation of already posted Payroll cost; never a payroll GL repost.</summary>
[ApiController, Route("api/facilities/labour-allocations"), Authorize,
 RequireModule("FACILITIES"), RequireModule("FINANCE"), AutoValidateAntiforgeryToken,
 EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FacilityLabourCostsApiController(
    FacilityLabourCostService service) : ControllerBase, IAsyncActionFilter
{
    private bool BindSession()
    {
        var principal = ControllerContext?.HttpContext?.User;
        var sid = principal?.FindFirstValue(AuthSessionRegistry.ClaimType);
        var tenant = principal?.FindFirstValue("TenantId");
        if (sid is not { Length: 48 } ||
            !long.TryParse(tenant, out var tenantId) || tenantId <= 0)
            return false;
        Response.Headers["X-EduOS-Session-Reference"] = AuthSessionRegistry.ToPublicReference(sid);
        Response.Headers["X-EduOS-Tenant-Id"] = tenant;
        return true;
    }
    private IActionResult SessionUnavailable() =>
        Unauthorized(new { success = false, message = "Session unavailable; sign in again." });
    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!BindSession()) { context.Result = SessionUnavailable(); return; }
        await next();
    }
    private IActionResult Reply<T>(ApiResponse<T> result) =>
        BindSession() ? StatusCode(result.StatusCode, result) : SessionUnavailable();

    [HttpGet("eligible-sources"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> EligibleSources(long? campusId, string? search,
        int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        Reply(await service.EligibleSourcesAsync(campusId, search, page, pageSize, ct));

    [HttpGet("{reference:guid}"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Get(Guid reference, CancellationToken ct) =>
        Reply(await service.GetAsync(reference, ct));

    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> List(long? campusId, int page = 1,
        int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.ListAsync(campusId, page, pageSize, ct));

    [HttpPost, Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> Create([FromBody] CreateFacilityLabourAllocationDto request,
        CancellationToken ct) => Reply(await service.CreateAsync(request, ct));

    [HttpPost("{reference:guid}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Review(Guid reference,
        [FromBody] ReviewFacilityLabourAllocationDto request, CancellationToken ct) =>
        Reply(await service.ReviewAsync(reference, request, ct));
}
