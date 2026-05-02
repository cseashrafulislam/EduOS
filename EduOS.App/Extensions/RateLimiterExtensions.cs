using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

namespace EduOS.App.Extensions
{
    public static class RateLimiterExtensions
    {
        public static IServiceCollection AddRateLimiterConfiguration(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                // Login - prevent brute force (5 attempts per minute per IP)
                options.AddFixedWindowLimiter("LoginPolicy", opt =>
                {
                    opt.PermitLimit = 5;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });

                // Signup - prevent spam (3 per 5 minutes per IP)
                options.AddFixedWindowLimiter("SignupPolicy", opt =>
                {
                    opt.PermitLimit = 3;
                    opt.Window = TimeSpan.FromMinutes(5);
                    opt.QueueLimit = 0;
                });

                // Forgot password (3 per 10 minutes per IP)
                options.AddFixedWindowLimiter("ForgotPasswordPolicy", opt =>
                {
                    opt.PermitLimit = 3;
                    opt.Window = TimeSpan.FromMinutes(10);
                    opt.QueueLimit = 0;
                });

                // API - general rate limit (60 requests per minute per IP)
                options.AddFixedWindowLimiter("ApiPolicy", opt =>
                {
                    opt.PermitLimit = 60;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 10;
                });

                // Payment callback - more lenient (gateway may retry)
                options.AddFixedWindowLimiter("PaymentCallbackPolicy", opt =>
                {
                    opt.PermitLimit = 100;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 20;
                });

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
    }
}
