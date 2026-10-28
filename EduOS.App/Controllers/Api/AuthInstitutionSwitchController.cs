using EduOS.App.Security;
using EduOS.Core.DTOs.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EduOS.App.Controllers.Api;

public partial class AuthController
{
    // Tenant choices come exclusively from the current account's active memberships.
    [Authorize]
    [HttpGet("institutions")]
    public async Task<IActionResult> GetAvailableInstitutions()
    {
        Response.Headers.CacheControl = "no-store";
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized(new { success = false, message = "Session unavailable." });
        if (await TenantRoleResolver.IsPlatformAdminAsync(_db, user.Id))
            return Ok(new { success = true, data = new {
                currentTenantId = (long?)null, institutions = Array.Empty<TenantChoice>()
            } });
        var session = await GetSessionContextAsync(user);
        var currentId = long.TryParse(User.FindFirstValue("TenantId"), out var parsed) ? parsed : (long?)null;
        return Ok(new { success = true, data = new {
            currentTenantId = currentId,
            institutions = session.Tenants.Select(t => new { id = t.Id, name = t.Name })
        } });
    }

    // This read-only, no-store snapshot is used by already-open tabs to detect
    // a cookie switch/logout before showing old institution data again.
    [Authorize]
    [HttpGet("session-context")]
    public IActionResult CurrentSessionContext()
    {
        Response.Headers.CacheControl = "no-store";
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        if (sid is not { Length: 48 })
            return Unauthorized(new { success = false, message = "Session unavailable." });
        var tenantId = long.TryParse(User.FindFirstValue("TenantId"), out var parsed) && parsed > 0
            ? (long?)parsed : null;
        return Ok(new { success = true, data = new
        {
            sessionReference = AuthSessionRegistry.ToPublicReference(sid),
            tenantId
        } });
    }

    // Switching issues a fresh cookie with server-resolved roles and a new tenant-bound SID.
    // A posted TenantId is only a request for selection, never proof of authorization.
    [Authorize]
    [EnableRateLimiting("AccountSecurityPolicy")]
    [HttpPost("institutions/switch")]
    public async Task<IActionResult> SwitchInstitution([FromBody] SwitchInstitutionRequestDto request)
    {
        Response.Headers.CacheControl = "no-store";
        if (request == null || request.TenantId <= 0)
            return BadRequest(new { success = false, message = "Select a valid institution." });
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized(new { success = false, message = "Session unavailable." });
        if (await TenantRoleResolver.IsPlatformAdminAsync(_db, user.Id))
            return StatusCode(403, new { success = false, message = "Platform accounts cannot switch institution sessions." });
        var claim = User.FindFirstValue("TenantId");
        if (!long.TryParse(claim, out var currentTenantId) || currentTenantId <= 0 ||
            !await AuthSessionRegistry.IsValidAsync(_db, user.Id,
                User.FindFirstValue(AuthSessionRegistry.ClaimType), currentTenantId))
            return Unauthorized(new { success = false, message = "Session changed. Sign in again." });
        if (user.TwoFactorEnabled && !User.HasClaim("amr", "mfa"))
            return StatusCode(403, new { success = false, message = "Sign in with MFA before switching institutions." });

        var selected = await GetSessionContextAsync(user, request.TenantId);
        if (!selected.IsValid || selected.TenantId != request.TenantId)
            return StatusCode(403, new { success = false, message = "Institution membership is unavailable." });
        if (currentTenantId == request.TenantId)
            return Ok(new { success = true, data = new {
                tenantId = currentTenantId, redirectUrl = GetRedirectUrl(selected.Role, selected.TenantId)
            } });

        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authentication.Succeeded)
            return Unauthorized(new { success = false, message = "Session changed. Sign in again." });
        // Commit the old-SID deletion and new-SID insertion before issuing a cookie.
        // A concurrent request from another tab must not create a second valid switch.
        string? rotatedSid;
        try
        {
            rotatedSid = await AuthSessionRegistry.RotateAsync(_db, user.Id,
                User.FindFirstValue(AuthSessionRegistry.ClaimType), currentTenantId,
                selected.TenantId.Value, GetClientIp(),
                HttpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? "",
                authentication.Properties?.IsPersistent == true, HttpContext.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { success = false, message = "Institution session changed. Sign in again." });
        }
        if (rotatedSid == null)
            return Conflict(new { success = false, message = "Institution session changed. Sign in again." });

        await SignInSessionAsync(user, authentication.Properties?.IsPersistent == true,
            User.HasClaim("amr", "mfa") ? "mfa" : "pwd", selected, rotatedSid);
        Response.Headers["Clear-Site-Data"] = "\"cache\"";
        _logger.LogInformation("User {UserId} switched to institution {TenantId}", user.Id, selected.TenantId);
        return Ok(new { success = true, data = new {
            tenantId = selected.TenantId, redirectUrl = GetRedirectUrl(selected.Role, selected.TenantId)
        } });
    }
}
