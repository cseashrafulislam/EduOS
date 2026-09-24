using Microsoft.AspNetCore.Http;

namespace EduOS.App.Middleware
{
    /// <summary>
    /// Adds browser and transport-adjacent response hardening without changing application payloads.
    /// Keep policy here conservative: EduOS still has legacy pages that may use inline script/style.
    /// </summary>
    public sealed class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";

            // Do not emit the obsolete X-XSS-Protection parser switch. Modern browsers ignore it,
            // and old implementations have historically introduced their own XSS edge cases.
            headers["X-XSS-Protection"] = "0";

            // Hosting layers can append identifying headers after middleware has run, so strip the
            // application-level variants at the last possible point in the response lifecycle.
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.Remove("X-Powered-By");
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }

    public static class SecurityHeadersMiddlewareExtensions
    {
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        {
            return app.UseMiddleware<SecurityHeadersMiddleware>();
        }
    }
}
