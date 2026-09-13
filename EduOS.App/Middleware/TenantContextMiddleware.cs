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

            if (context.User.IsInRole("SuperAdmin"))
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

                // Resolve canonical membership on every request. Tenant assignments are security-sensitive
                // and must not remain stale after an administrator moves/disables a user.
                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user == null)
                {
                    _logger.LogWarning("Authenticated principal references missing user {UserId}.", userId);
                    await RejectAsync(context, StatusCodes.Status401Unauthorized, "Your session is no longer valid. Please sign in again.");
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
                // Tenant resolution is an authorization boundary. Never continue without a trusted tenant
                // context when the authenticated non-platform request cannot be resolved safely.
                _logger.LogError(ex, "Tenant context resolution failed for authenticated request.");
                if (!context.Response.HasStarted)
                    await RejectAsync(context, StatusCodes.Status503ServiceUnavailable, "Unable to validate your institution access right now. Please try again.");
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
