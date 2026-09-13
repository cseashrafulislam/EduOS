using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace EduOS.App.Middleware
{
    public class TenantContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantContextMiddleware> _logger;

        public TenantContextMiddleware(
            RequestDelegate next,
            ILogger<TenantContextMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            EduOSDbContext dbContext)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            try
            {
                var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!long.TryParse(userIdStr, out var userId) || userId <= 0)
                {
                    _logger.LogWarning("Authenticated request has no valid user identifier.");
                    await RejectAsync(context, StatusCodes.Status401Unauthorized, "Your session is invalid. Please sign in again.");
                    return;
                }

                // Resolve canonical account state on every authenticated request. Tenant assignments and
                // account activation are security-sensitive and must not remain stale in an old cookie.
                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user == null)
                {
                    _logger.LogWarning("Authenticated principal references missing user {UserId}.", userId);
                    await RejectAsync(context, StatusCodes.Status401Unauthorized, "Your session is no longer valid. Please sign in again.");
                    return;
                }

                if (!user.IsActive)
                {
                    _logger.LogWarning("Deactivated user {UserId} attempted to use an authenticated session.", userId);
                    await RejectAsync(context, StatusCodes.Status403Forbidden, "Your account has been deactivated. Please contact support.");
                    return;
                }

                // Platform administrators are intentionally tenantless, but they still pass the canonical
                // account existence/activation checks above before bypassing tenant resolution.
                if (context.User.IsInRole("SuperAdmin"))
                {
                    await _next(context);
                    return;
                }

                if (user.TenantId is not long tenantId || tenantId <= 0)
                {
                    _logger.LogWarning("User {UserId} has no valid tenant assignment.", userId);
                    await RejectAsync(context, StatusCodes.Status403Forbidden, "Your account is not assigned to an active institution.");
                    return;
                }

                // Tenant state is deliberately checked per request. A disabled/deleted tenant must stop
                // receiving traffic immediately instead of remaining authorized through a stale cache.
                var tenantActive = await dbContext.Tenants
                    .AsNoTracking()
                    .AnyAsync(t => t.Id == tenantId && t.IsActive && !t.IsDeleted);

                if (!tenantActive)
                {
                    _logger.LogWarning("User {UserId} has inactive/deleted tenant {TenantId}.", userId, tenantId);
                    await RejectAsync(context, StatusCodes.Status403Forbidden, "Your institution account is currently inactive. Please contact support.");
                    return;
                }

                context.Items["TenantId"] = tenantId;
                await _next(context);
            }
            catch (Exception ex)
            {
                // Tenant/account resolution is an authorization boundary. Never continue without trusted
                // canonical state when an authenticated request cannot be resolved safely.
                _logger.LogError(ex, "Authenticated account or tenant context resolution failed.");
                if (!context.Response.HasStarted)
                    await RejectAsync(context, StatusCodes.Status503ServiceUnavailable, "Unable to validate your account access right now. Please try again.");
            }
        }

        private static async Task RejectAsync(HttpContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new { success = false, message });
        }
    }

    public static class TenantContextMiddlewareExtensions
    {
        public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
        {
            return app.UseMiddleware<TenantContextMiddleware>();
        }
    }
}
