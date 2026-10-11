using EduOS.App.Security;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace EduOS.App.Middleware;

/// <summary>Reject revoked, expired and pre-session-registry cookies before tenant authorization.</summary>
public sealed class AuthSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, EduOSDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var sid = context.User.FindFirstValue(AuthSessionRegistry.ClaimType);
        var tenantText = context.User.FindFirstValue("TenantId");
        long? tenantId = null;
        if (tenantText != null)
        {
            if (!long.TryParse(tenantText, out var parsed) || parsed <= 0)
            {
                await RejectAsync(context);
                return;
            }
            tenantId = parsed;
        }
        if (!long.TryParse(id, out var userId) ||
            !await AuthSessionRegistry.IsValidAsync(db, userId, sid, tenantId))
        {
            await RejectAsync(context);
            return;
        }
        await next(context);
    }

    private static async Task RejectAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Session expired or revoked. Please sign in again." });
        }
        else context.Response.Redirect("/Account/Login");
    }
}

public static class AuthSessionMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthSessionGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<AuthSessionMiddleware>();
}
