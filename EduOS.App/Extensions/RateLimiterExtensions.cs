using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace EduOS.App.Extensions
{
    public static class RateLimiterExtensions
    {
        public static IServiceCollection AddRateLimiterConfiguration(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                // Every policy is partitioned. A non-partitioned limiter would make
                // one busy client consume the quota for the entire application.
                options.AddPolicy("LoginPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(5, TimeSpan.FromMinutes(1))));

                // Signup - prevent spam (3 per 5 minutes per IP)
                options.AddPolicy("SignupPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(3, TimeSpan.FromMinutes(5))));

                // Forgot password (3 per 10 minutes per IP)
                options.AddPolicy("ForgotPasswordPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(3, TimeSpan.FromMinutes(10))));

                // API - general rate limit (60 requests per minute per IP)
                options.AddPolicy("ApiPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(60, TimeSpan.FromMinutes(1), 10)));

                // Government identifier matching is deliberately much tighter than
                // the general API quota to reduce enumeration attempts.
                options.AddPolicy("LearnerIdentityPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(10, TimeSpan.FromMinutes(1))));

                // Payment callback - more lenient (gateway may retry)
                options.AddPolicy("PaymentCallbackPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetIpPartition(context),
                        _ => CreateOptions(100, TimeSpan.FromMinutes(1), 20)));

                // Global rejection response
                options.OnRejected = async (context, ct) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                    if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
                    {
                        await context.HttpContext.Response.WriteAsJsonAsync(new
                        {
                            success = false,
                            message = "Too many requests. Please try again later."
                        }, cancellationToken: ct);
                    }
                    else
                    {
                        context.HttpContext.Response.Redirect("/Error/429");
                    }
                };
            });

            return services;
        }

        private static FixedWindowRateLimiterOptions CreateOptions(
            int permitLimit,
            TimeSpan window,
            int queueLimit = 0)
        {
            return new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = queueLimit,
                AutoReplenishment = true
            };
        }

        private static string GetClientPartition(HttpContext context)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrWhiteSpace(userId)
                ? GetIpPartition(context)
                : $"user:{userId}";
        }

        private static string GetIpPartition(HttpContext context)
        {
            return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        }
    }
}
