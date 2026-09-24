using System.Security.Claims;

namespace EduOS.App.Middleware;

/// <summary>
/// Privileged cookie sessions must carry an MFA authentication-method claim.
/// Password-only privileged sessions can reach only MFA bootstrap, authentication,
/// error, health and required static-resource paths.
/// </summary>
public sealed class PrivilegedMfaMiddleware
{
    private static readonly PathString[] AllowedPaths =
    [
        "/Account/MfaSetup",
        "/Account/MfaChallenge",
        "/Account/Login",
        "/Account/Logout",

        "/api/auth/login",
        "/api/auth/logout",
        "/api/auth/mfa",
        "/api/auth/forgot-password",
        "/api/auth/reset-password",

        "/Localization",
        "/Error",
        "/health",
        "/hangfire",

        "/css",
        "/js",
        "/lib",
        "/images",
        "/img",

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

        if (user.Identity?.IsAuthenticated != true ||
            !IsPrivileged(user) ||
            HasMfaSession(user) ||
            IsAllowed(context.Request.Path))
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

    private static bool IsPrivileged(ClaimsPrincipal user)
    {
        return user.IsInRole("SuperAdmin") ||
               user.IsInRole("TenantAdmin") ||
               user.IsInRole("AdmissionOfficer");
    }

    private static bool HasMfaSession(ClaimsPrincipal user)
    {
        return user.Claims.Any(x =>
            x.Type == "amr" &&
            string.Equals(x.Value, "mfa", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowed(PathString path)
    {
        return AllowedPaths.Any(allowed => path.StartsWithSegments(allowed));
    }
}

public static class PrivilegedMfaMiddlewareExtensions
{
    public static IApplicationBuilder UsePrivilegedMfa(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PrivilegedMfaMiddleware>();
    }
}