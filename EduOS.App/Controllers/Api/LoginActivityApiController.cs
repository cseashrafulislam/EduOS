using EduOS.App.Security;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Security.Claims;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/account/login-activity")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoginActivityApiController(ILoginActivityService service, EduOSDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool? success = null, [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, [FromQuery] bool unassigned = false,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetMyHistoryAsync(page, pageSize, success, cancellationToken, from, to, unassigned);
        // A concurrent institution switch must not return a stale report.
        if (!await CurrentSessionValidAsync())
            return Unauthorized(new { success = false, message = "Session changed. Sign in again." });
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] bool? success = null,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] bool unassigned = false,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ExportMyHistoryCsvAsync(success, cancellationToken, from, to, unassigned);
        if (!await CurrentSessionValidAsync())
            return Unauthorized(new { success = false, message = "Session changed. Sign in again." });
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8",
            "sign-in-activity-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".csv");
    }

    private async Task<bool> CurrentSessionValidAsync()
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantText = User.FindFirstValue("TenantId");
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        return long.TryParse(userIdText, out var userId) && userId > 0 &&
            long.TryParse(tenantText, out var tenantId) && tenantId > 0 &&
            await AuthSessionRegistry.IsValidAsync(db, userId, sid, tenantId);
    }
}
