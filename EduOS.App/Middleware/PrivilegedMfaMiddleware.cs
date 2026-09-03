using System.Security.Claims;

namespace EduOS.App.Middleware;

/// <summary>
/// Privileged cookie sessions must carry an MFA authentication-method claim.
/// Password-only sessions can reach only the setup, challenge and sign-out paths.
/// </summary>
public sealed class PrivilegedMfaMiddleware
{
    private static readonly string[] AllowedPrefixes =
    [
        "/Account/Mfa",
        "/Account/Login",
        "/Account/Logout",
        "/api/auth/login",
        "/api/auth/mfa",
        "/api/auth/logout",
        "/api/auth/forgot-password",
        "/api/auth/reset-password",
        "/Localization/",
        "/Error/",
        "/css/",
        "/js/",
        "/lib/",
        "/images/",
        "/img/",
        "/favicon.ico",
        "/manifest.webmanifest",
        "/service-worker.js"
    ];

    private readonly RequestDelegate _next;

    public PrivilegedMfaMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true
            || (!user.IsInRole("SuperAdmin") && !user.IsInRole("TenantAdmin"))
            || user.Claims.Any(x => x.Type == "amr" && x.Value == "mfa")
            || IsAllowed(context.Request.Path))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                code = "MFA_REQUIRED",
                message = "Multi-factor authentication is required.",
                redirectUrl = "/Account/MfaSetup"
            });
            return;
        }

        context.Response.Redirect("/Account/MfaSetup");
    }

    private static bool IsAllowed(PathString path) =>
        AllowedPrefixes.Any(prefix => path.StartsWithSegments(prefix));
}

public static class PrivilegedMfaMiddlewareExtensions
{
    public static IApplicationBuilder UsePrivilegedMfa(this IApplicationBuilder app) =>
        app.UseMiddleware<PrivilegedMfaMiddleware>();
}
