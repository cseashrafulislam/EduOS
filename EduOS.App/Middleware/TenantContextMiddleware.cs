using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace EduOS.App.Middleware
{
    public class TenantContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantContextMiddleware> _logger;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

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
            EduOSDbContext dbContext,
            IMemoryCache cache)
        {
            // Anonymous requests pass through
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            // SuperAdmin doesn't need tenant context
            if (context.User.IsInRole("SuperAdmin"))
            {
                await _next(context);
                return;
            }

            try
            {
                var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!long.TryParse(userIdStr, out var userId))
                {
                    await _next(context);
                    return;
                }

                var cacheKey = $"tenant:user:{userId}";

                if (!cache.TryGetValue<long>(cacheKey, out var tenantId))
                {
                    var user = await userManager.FindByIdAsync(userId.ToString());
                    if (user == null || user.TenantId == null)
                    {
                        await _next(context);
                        return;
                    }

                    tenantId = user.TenantId.Value;

                    if (tenantId > 0)
                    {
                        var tenantActive = await dbContext.Tenants
                            .AsNoTracking()
                            .Where(t => t.Id == tenantId && t.IsActive && !t.IsDeleted)
                            .AnyAsync();

                        if (!tenantActive)
                        {
                            _logger.LogWarning("User {UserId} has inactive/deleted tenant {TenantId}",
                                userId, tenantId);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                success = false,
                                message = "Your institution account is currently inactive. Please contact support."
                            });
                            return;
                        }

                        cache.Set(cacheKey, tenantId, CacheDuration);
                    }
                }

                context.Items["TenantId"] = tenantId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tenant context resolution failed");
            }

            await _next(context);
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
