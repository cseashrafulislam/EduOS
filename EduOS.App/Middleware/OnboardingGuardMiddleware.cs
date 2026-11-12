using EduOS.Core.Enums.Domain;
using EduOS.Core.Common;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.App.Middleware
{
    public class OnboardingGuardMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<OnboardingGuardMiddleware> _logger;

        private static readonly PathString[] AllowedPathPrefixes =
        [
            "/Account",
            "/Pricing",
            "/Error",
            "/api/auth",
            "/api/onboarding",
            "/api/tenant-profile",
            "/api/tenant-settings",
            "/api/communication-admin/gateways",
            "/api/subscription",
            "/api/subscription-plans",
            "/api/subscription-payment",
            "/api/institution-onboarding",
            "/api/tenant-modules",
            "/uploads",
            "/css",
            "/js",
            "/lib",
            "/images",
            "/img",
            "/favicon.ico",
            "/manifest.webmanifest",
            "/service-worker.js",
            "/health",
            "/hangfire"
        ];

        public OnboardingGuardMiddleware(RequestDelegate next, ILogger<OnboardingGuardMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, EduOSDbContext dbContext)
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

            var path = context.Request.Path;

            if (IsAllowed(path) ||
                (HttpMethods.IsGet(context.Request.Method) && path.StartsWithSegments("/api/platform-catalog")))
            {
                await _next(context);
                return;
            }

            try
            {
                if (context.Items["TenantId"] is not long tenantId || tenantId <= 0)
                {
                    _logger.LogWarning("Onboarding guard received authenticated request without trusted tenant context for path {Path}.", path);
                    await RejectAsync(context, StatusCodes.Status403Forbidden, "A valid institution context is required.");
                    return;
                }

                var tenant = await dbContext.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);
                if (tenant == null)
                {
                    _logger.LogWarning("Onboarding guard could not resolve active tenant {TenantId} for path {Path}.", tenantId, path);
                    await RejectAsync(context, StatusCodes.Status403Forbidden, "Your institution account is not available.");
                    return;
                }

                var complete = tenant.OnboardingCompletedAt.HasValue && tenant.OnboardingStage == OnboardingStage.Completed;
                if (tenant.State is TenantState.Suspended or TenantState.Closed ||
                    (tenant.OnboardingStage == OnboardingStage.Completed && (tenant.State != TenantState.Active || !complete)))
                {
                    await RejectAsync(context, StatusCodes.Status403Forbidden, "Your institution account is not active.");
                    return;
                }

                if (tenant.State == TenantState.Active && complete)
                {
                    await _next(context);
                    return;
                }

                // Post-payment workspace access is independent from completing optional
                // institution setup, but requires every request to pass verified,
                // current tenant identity and active paid/trial subscription checks.
                if (tenant.State == TenantState.Active &&
                    tenant.OnboardingStage >= OnboardingStage.CampusSetup)
                {
                    var subscription = await dbContext.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
                        .Where(s => s.TenantId == tenantId && !s.IsDeleted &&
                            s.StartsAt <= DateTime.UtcNow && s.EndsAt > DateTime.UtcNow &&
                            (s.State == SubscriptionState.Active || s.State == SubscriptionState.Trial))
                        .OrderByDescending(s => s.StartsAt).FirstOrDefaultAsync();
                    if (OnboardingWorkspacePolicy.CanUse(tenant, subscription, DateTime.UtcNow))
                    {
                        await _next(context);
                        return;
                    }
                }

                var redirectUrl = GetRedirectUrlForStep(tenant.OnboardingStage);

                if (path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.Headers["X-Onboarding-Required"] = "true";
                    context.Response.Headers["X-Onboarding-Step"] = ((int)tenant.OnboardingStage).ToString();
                    await context.Response.WriteAsJsonAsync(new { success = false, message = "Please complete onboarding first", redirectUrl });
                    return;
                }

                _logger.LogDebug("Redirecting tenant {TenantId} to onboarding step {Step}", tenantId, tenant.OnboardingStage);
                context.Response.Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Onboarding guard failed for path {Path}", path);
                if (!context.Response.HasStarted)
                    await RejectAsync(context, StatusCodes.Status503ServiceUnavailable, "Unable to validate onboarding status right now. Please try again.");
            }
        }

        private static bool IsAllowed(PathString path) => AllowedPathPrefixes.Any(prefix => path.StartsWithSegments(prefix));

        private static async Task RejectAsync(HttpContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new { success = false, message });
        }

        private static string GetRedirectUrlForStep(OnboardingStage step) => step switch
        {
            OnboardingStage.EmailVerification => "/Account/VerifyEmail",
            OnboardingStage.InstitutionProfile => "/Account/InstitutionProfile",
            OnboardingStage.PlanSelection => "/Account/PlanSelection",
            OnboardingStage.Payment => "/Account/Payment",
            OnboardingStage.CampusSetup => "/Account/CampusSetup",
            OnboardingStage.AcademicSetup => "/Account/AcademicSetup",
            OnboardingStage.ModuleSetup => "/Account/ModuleSetup",
            OnboardingStage.BrandingSetup => "/Account/BrandingSetup",
            OnboardingStage.GeneralSettings => "/Account/GeneralSettings",
            OnboardingStage.GatewaySetup => "/Account/GatewaySetup",
            _ => "/Account/InstitutionProfile"
        };

        private sealed class OnboardingState
        {
            public TenantState State { get; set; }
            public bool IsComplete { get; set; }
            public OnboardingStage Step { get; set; }
        }
    }

    public static class OnboardingGuardMiddlewareExtensions
    {
        public static IApplicationBuilder UseOnboardingGuard(this IApplicationBuilder app) => app.UseMiddleware<OnboardingGuardMiddleware>();
    }
}