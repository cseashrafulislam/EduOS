using EduOS.App.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace EduOS.App.Middleware;

/// <summary>
/// The MVC ResponseCache attribute cannot run when authentication, module
/// authorization or rate limiting short-circuits a private V2 request.
/// Register immediately after UseRouting, before any authorization middleware.
/// Never cache private responses, including 401/403/404/409/429 and exports.
/// </summary>
public static class V2PrivateApiResponseHeadersExtensions
{
    private static readonly string[] Prefixes =
    [
        "/api/lms",
        "/api/library",
        "/api/hostel",
        "/api/transport",
        "/api/guardian-hostel",
        "/api/guardian-transport",
        "/api/parent-transport"
    ];

    public static IApplicationBuilder UseV2PrivateApiResponseHeaders(
        this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value;
        // Endpoint metadata is available after UseRouting even when authentication
        // short-circuits MVC. Protect conventional Razor/print routes as well as APIs.
        // This is deliberately scoped to the four V2 modules, not all public pages.
        var v2ModuleEndpoint = context.GetEndpoint()?.Metadata
            .GetOrderedMetadata<RequireModuleAttribute>()
            .Any(attribute => attribute.Policy is
                "EduOSModule:LMS" or "EduOSModule:LIBRARY" or
                "EduOSModule:HOSTEL" or "EduOSModule:TRANSPORT") == true;
        if (v2ModuleEndpoint || (path != null && Prefixes.Any(prefix =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                return Task.CompletedTask;
            });
        }

        await next();
    });
}
