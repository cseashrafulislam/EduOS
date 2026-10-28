using EduOS.App.Security;
using EduOS.Core.DTOs.SaaS;
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
public sealed class WorkspacePreferencesController(IWorkspacePreferencesService service) : Controller
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

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("/api/workspace-preferences")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!BindCurrentSession()) return Unauthorized(new { success = false, message = "Session unavailable." });
        var result = await service.GetAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("/api/workspace-preferences")]
    public async Task<IActionResult> Save([FromBody] SaveWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!BindCurrentSession()) return Unauthorized(new { success = false, message = "Session unavailable." });
        var result = await service.SaveAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("/api/workspace-preferences/reset")]
    public async Task<IActionResult> Reset([FromBody] ResetWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!BindCurrentSession()) return Unauthorized(new { success = false, message = "Session unavailable." });
        var result = await service.ResetAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
