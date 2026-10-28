using EduOS.App.Security;
using EduOS.Core.Common;
using System.Data;
using EduOS.Core.DTOs.SaaS;
using EduOS.Persistence.Context;
using System.Security.Claims;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("workspace")]
public sealed class WorkspacePreferencesController(IWorkspacePreferencesService service, EduOSDbContext db) : Controller
{
    // Bind preference responses to the exact cookie session and institution that requested them.
    // A stale tab must not apply another institution's appearance after a cookie switch.
    private bool BindCurrentSession()
    {
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        if (sid is not { Length: 48 }) return false;
        Response.Headers["X-EduOS-Session-Reference"] = AuthSessionRegistry.ToPublicReference(sid);
        Response.Headers["X-EduOS-Tenant-Id"] = User.FindFirstValue("TenantId") ?? string.Empty;
        return true;
    }

    // The session may be revoked or switched while a database query is in flight.
    // Never return a saved user/institution preference document after that revocation.
    private async Task<bool> SessionStillValidAsync()
    {
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        return long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) &&
            userId > 0 &&
            long.TryParse(User.FindFirstValue("TenantId"), out var tenantId) &&
            tenantId > 0 &&
            await AuthSessionRegistry.IsValidAsync(db, userId, sid, tenantId);
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("/api/workspace-preferences")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!BindCurrentSession()) return Unauthorized(new { success = false, message = "Session unavailable." });
        var result = await service.GetAsync(cancellationToken);
        if (!await SessionStillValidAsync()) return Unauthorized(new { success = false, message = "Session changed. Reload and sign in again." });
        return StatusCode(result.StatusCode, result);
    }

    // A session can be revoked after the request middleware but before a preference
    // write reaches SaveChanges. SERIALIZABLE holds the session-token read lock through
    // the preference commit: either revocation wins and no preference is written, or
    // the preference commits before revocation. A post-write mismatch rolls back BOTH.
    private async Task<IActionResult> WriteInCurrentSessionAsync(
        Func<CancellationToken, Task<ApiResponse<WorkspacePreferencesDto>>> write,
        CancellationToken cancellationToken)
    {
        if (!BindCurrentSession())
            return Unauthorized(new { success = false, message = "Session unavailable." });

        try
        {
            var result = await db.ExecuteInTransactionAsync(async ct =>
            {
                if (!await SessionStillValidAsync())
                    throw new SessionChangedDuringWriteException();

                var response = await write(ct);
                if (!response.Success)
                    throw new WorkspaceWriteRejectedException(response);

                if (!await SessionStillValidAsync())
                    throw new SessionChangedDuringWriteException();

                return response;
            }, IsolationLevel.Serializable, cancellationToken);

            return StatusCode(result.StatusCode, result);
        }
        catch (SessionChangedDuringWriteException)
        {
            return Unauthorized(new { success = false, message = "Session changed. Reload and sign in again." });
        }
        catch (WorkspaceWriteRejectedException ex)
        {
            return StatusCode(ex.Response.StatusCode, ex.Response);
        }
    }

    private sealed class SessionChangedDuringWriteException : Exception;

    private sealed class WorkspaceWriteRejectedException(ApiResponse<WorkspacePreferencesDto> response) : Exception
    {
        public ApiResponse<WorkspacePreferencesDto> Response { get; } = response;
    }

    [HttpPut("/api/workspace-preferences")]
    public Task<IActionResult> Save([FromBody] SaveWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken) =>
        WriteInCurrentSessionAsync(ct => service.SaveAsync(request, ct), cancellationToken);

    [HttpPost("/api/workspace-preferences/reset")]
    public Task<IActionResult> Reset([FromBody] ResetWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken) =>
        WriteInCurrentSessionAsync(ct => service.ResetAsync(request, ct), cancellationToken);
}
