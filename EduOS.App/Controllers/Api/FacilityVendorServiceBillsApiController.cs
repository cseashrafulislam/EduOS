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

/// <summary>
/// Separate non-stock vendor service bills, without changing stock-backed
/// procurement invoice routes or bypassing the canonical Finance GL.
/// </summary>
[ApiController, Route("api/facilities/vendor-service-bills"), Authorize,
 RequireModule("FACILITIES"), AutoValidateAntiforgeryToken,
 EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FacilityVendorServiceBillsApiController(
    FacilityVendorServiceBillService service) : ControllerBase, IAsyncActionFilter
{
    // Bind every private JSON response to the current authenticated institution
    // and public session reference, never just an old cookie role claim.
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

    [HttpGet("verified-work"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> VerifiedWork(long? campusId, string? search,
        int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        Reply(await service.VerifiedChoicesAsync(campusId, search, page, pageSize, ct));

    [HttpGet("{reference:guid}"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Get(Guid reference, CancellationToken ct) =>
        Reply(await service.GetAsync(reference, ct));

    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> List(long? campusId, string? state, int page = 1,
        int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.ListAsync(campusId, state, page, pageSize, ct));

    [HttpPost, Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> Create([FromBody] CreateFacilityVendorServiceBillDto request,
        CancellationToken ct) => Reply(await service.CreateAsync(request, ct));

    [HttpPost("{reference:guid}/submit"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> Submit(Guid reference,
        [FromBody] FacilityVendorBillSubmitDto request, CancellationToken ct) =>
        Reply(await service.SubmitAsync(reference, request, ct));

    [HttpPost("{reference:guid}/reversal"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestReversal(Guid reference,
        [FromBody] FacilityVendorBillReversalRequestDto request, CancellationToken ct) =>
        Reply(await service.RequestReversalAsync(reference, request, ct));

    [HttpPost("{reference:guid}/reversal/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewReversal(Guid reference,
        [FromBody] FacilityVendorBillReversalReviewDto request, CancellationToken ct) =>
        Reply(await service.ReviewReversalAsync(reference, request, ct));

    [HttpPost("{reference:guid}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Review(Guid reference,
        [FromBody] FacilityVendorBillReviewDto request, CancellationToken ct) =>
        Reply(await service.ReviewAsync(reference, request, ct));
}
