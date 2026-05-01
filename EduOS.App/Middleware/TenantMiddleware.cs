using EduOS.Core.Common;
using EduOS.Core.Interfaces.IRepositories;

namespace EduOS.App.Middleware
{
    /// <summary>
    /// Middleware to validate tenant context for incoming requests.
    /// Should be placed AFTER UseAuthentication and BEFORE controllers.
    /// </summary>
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantMiddleware> _logger;

        // Endpoints that don't require tenant validation
        private static readonly string[] _publicPaths = new[]
        {
            "/api/v1/auth/login",
            "/api/v1/auth/register",
            "/api/v1/auth/forgot-password",
            "/api/v1/auth/reset-password",
            "/api/v1/tenants/register",
            "/swagger",
            "/health",
            "/hangfire"
        };

        public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ICurrentUserService currentUser,
            IUnitOfWork unitOfWork)
        {
            var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

            // Skip validation for public endpoints
            if (_publicPaths.Any(p => path.StartsWith(p)))
            {
                await _next(context);
                return;
            }

            // For authenticated requests, validate tenant
            if (currentUser.IsAuthenticated)
            {
                // SuperAdmin can access without tenant
                if (currentUser.IsSuperAdmin)
                {
                    await _next(context);
                    return;
                }

                // Verify tenant exists and is active
                if (currentUser.TenantId <= 0)
                {
                    await WriteErrorResponse(context, 400, "Tenant context is required");
                    return;
                }

                var tenant = await unitOfWork.Tenants.GetByIdAsync(currentUser.TenantId);

                if (tenant == null)
                {
                    await WriteErrorResponse(context, 404, "Tenant not found");
                    return;
                }

                if (!tenant.IsActive)
                {
                    await WriteErrorResponse(context, 403, "Tenant account is inactive. Please contact support.");
                    return;
                }

                _logger.LogDebug("Tenant {TenantId} validated for User {UserId}",
                    currentUser.TenantId, currentUser.UserId);
            }

            await _next(context);
        }

        private static async Task WriteErrorResponse(HttpContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message,
                statusCode
            });
        }
    }
}