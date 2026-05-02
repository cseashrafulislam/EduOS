using Microsoft.AspNetCore.Http;

namespace EduOS.App.Middleware
{
    /// <summary>
    /// Adds security-related HTTP headers to all responses.
    /// Helps protect against XSS, clickjacking, MIME-sniffing, etc.
    /// </summary>
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;

            // Prevent MIME-type sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // Prevent clickjacking - allow same-origin only
            headers["X-Frame-Options"] = "SAMEORIGIN";

            // XSS protection (legacy browsers)
            headers["X-XSS-Protection"] = "1; mode=block";

            // Referrer policy
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Permissions policy - lock down unused features
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // Remove server header for security
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

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
