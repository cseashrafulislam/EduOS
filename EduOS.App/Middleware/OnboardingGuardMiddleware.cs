using EduOS.Core.Enums;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EduOS.App.Middleware
{
    /// <summary>
    /// Redirects authenticated tenant users to the onboarding wizard
    /// if they haven't completed it yet. Allows specific paths through.
    /// </summary>
    public class OnboardingGuardMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<OnboardingGuardMiddleware> _logger;

        // Paths that bypass the onboarding check
        private static readonly string[] _allowedPathPrefixes = new[]
        {
            "/Account/",
            "/Pricing",
            "/Error/",
            "/api/auth",
            "/api/onboarding",
            "/api/tenant-profile",
            "/api/tenant-settings",
            "/api/subscription",
            "/api/subscription-plans",
            "/api/subscription-payment",
            "/api/institution-onboarding",
            "/api/tenant-modules",
            "/uploads/",
            "/css/",
            "/js/",
            "/lib/",
            "/images/",
            "/img/",
            "/favicon.ico",
            "/hangfire"
        };

        public OnboardingGuardMiddleware(
            RequestDelegate next,
            ILogger<OnboardingGuardMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            EduOSDbContext dbContext,
            IMemoryCache cache)
        {
            // Skip if not authenticated
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            // Skip for SuperAdmin
            if (context.User.IsInRole("SuperAdmin"))
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? "";

            // Pass through allowed paths
            foreach (var prefix in _allowedPathPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }
            }

            try
            {
                // Get tenant ID from HttpContext (set by TenantContextMiddleware)
                long tenantId = 0;
                if (context.Items["TenantId"] is long id)
                    tenantId = id;

                if (tenantId <= 0)
                {
                    await _next(context);
                    return;
                }

                // Check onboarding status - cached for performance
                var cacheKey = $"onboarding:tenant:{tenantId}";

                OnboardingState? state;
                if (!cache.TryGetValue(cacheKey, out state) || state == null)
                {
                    state = await dbContext.Tenants
                        .AsNoTracking()
                        .Where(t => t.Id == tenantId)
                        .Select(t => new OnboardingState
                        {
                            IsComplete = t.IsOnboardingComplete,
                            Step = t.OnboardingStep
                        })
                        .FirstOrDefaultAsync();

                    if (state != null)
                    {
                        // Cache for 2 min - short TTL because onboarding state changes often
                        cache.Set(cacheKey, state, TimeSpan.FromMinutes(2));
                    }
                }

                if (state == null || state.IsComplete)
                {
                    await _next(context);
                    return;
                }

                // Onboarding incomplete - redirect or block
                var redirectUrl = GetRedirectUrlForStep(state.Step);

                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.Headers["X-Onboarding-Required"] = "true";
                    context.Response.Headers["X-Onboarding-Step"] = ((int)state.Step).ToString();
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        message = "Please complete onboarding first",
                        redirectUrl
                    });
                    return;
                }

                _logger.LogDebug("Redirecting tenant {TenantId} to onboarding step {Step}",
                    tenantId, state.Step);
                context.Response.Redirect(redirectUrl);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Onboarding guard failed for path {Path}", path);
            }

            await _next(context);
        }

        private static string GetRedirectUrlForStep(OnboardingStep step) => step switch
        {
            OnboardingStep.EmailVerification => "/Account/VerifyEmail",
            OnboardingStep.InstitutionProfile => "/Account/InstitutionProfile",
            OnboardingStep.PlanSelection => "/Account/PlanSelection",
            OnboardingStep.Payment => "/Account/Payment",
            OnboardingStep.CampusSetup => "/Account/CampusSetup",
            OnboardingStep.AcademicSetup => "/Account/AcademicSetup",
            OnboardingStep.ModuleSetup => "/Account/ModuleSetup",
            OnboardingStep.BrandingSetup => "/Account/BrandingSetup",
            OnboardingStep.GeneralSettings => "/Account/GeneralSettings",
            OnboardingStep.GatewaySetup => "/Account/GatewaySetup",
            _ => "/Account/InstitutionProfile"
        };

        private class OnboardingState
        {
            public bool IsComplete { get; set; }
            public OnboardingStep Step { get; set; }
        }
    }

    public static class OnboardingGuardMiddlewareExtensions
    {
        public static IApplicationBuilder UseOnboardingGuard(this IApplicationBuilder app)
        {
            return app.UseMiddleware<OnboardingGuardMiddleware>();
        }
    }
}
