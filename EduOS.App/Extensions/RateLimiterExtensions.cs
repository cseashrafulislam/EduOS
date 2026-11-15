using Microsoft.AspNetCore.Http;
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
                // Shared school/campus NATs may have many independent users signing in
                // together. The per-account Identity lockout still limits guessing.
                options.AddPolicy("LoginPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetIpPartition(context),
                        _ => CreateOptions(120, TimeSpan.FromMinutes(1))));

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

                // Credential-changing endpoints must not inherit the generous
                // shared-IP login allowance. Partition by the signed-in user.
                options.AddPolicy("AccountSecurityPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(5, TimeSpan.FromMinutes(15))));

                // ERP pages legitimately load many independent read models.
                // Authenticated users have a tenant-aware READ allowance; writes
                // remain tightly bounded per account. Anonymous API reads keep
                // the old shared-IP limit to prevent enumeration.
                //
                // A request queue here caused hidden 30-60 second page stalls
                // after bursts. Reply 429 immediately when a genuine limit is
                // reached; the UI can then show a retryable error instead.
                options.AddPolicy("ApiPolicy", context =>
                {
                    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var authenticated = !string.IsNullOrWhiteSpace(userId);
                    var read = HttpMethods.IsGet(context.Request.Method) ||
                        HttpMethods.IsHead(context.Request.Method) ||
                        HttpMethods.IsOptions(context.Request.Method);
                    var partition = authenticated
                        ? $"tenant:{context.User.FindFirstValue("TenantId") ?? "0"}:user:{userId}"
                        : GetIpPartition(context);
                    var budget = authenticated && read ? 240 : 60;
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"api:{partition}:{(read ? "read" : "write")}",
                        _ => CreateOptions(budget, TimeSpan.FromMinutes(1)));
                });

                // Government identifier matching is deliberately much tighter than
                // the general API quota to reduce enumeration attempts.
                options.AddPolicy("LearnerIdentityPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(10, TimeSpan.FromMinutes(1))));

                options.AddPolicy("AdmissionIntakePolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(30, TimeSpan.FromMinutes(1), 5)));

                // Authenticated enrollment/configuration stays limited per account.
                options.AddPolicy("MfaPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartition(context),
                        _ => CreateOptions(5, TimeSpan.FromMinutes(15))));

                // Anonymous challenge completion has its own per-IP budget; invalid
                // codes are additionally limited by each user's Identity lockout.
                options.AddPolicy("MfaLoginPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetIpPartition(context),
                        _ => CreateOptions(120, TimeSpan.FromMinutes(1))));

                // Payment callback - more lenient (gateway may retry)
                options.AddPolicy("PaymentCallbackPolicy", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetIpPartition(context),
                        _ => CreateOptions(100, TimeSpan.FromMinutes(1), 20)));

                // Global rejection response
                options.OnRejected = async (context, ct) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.Headers.CacheControl = "no-store";

                    if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
                    {
                        await context.HttpContext.Response.WriteAsJsonAsync(new
                        {
                            success = false,
                            message = "Too many requests. Please try again later."
                        }, cancellationToken: ct);
                    }
                    // A redirect silently replaces HTTP 429 with HTTP 302,
                    // defeating retry/backoff handling and confusing clients.
                    // For browser requests, leave the response body empty so
                    // the existing StatusCodePages middleware renders /Error/429
                    // while preserving the original 429 status.
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
