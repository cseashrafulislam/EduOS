using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Extensions
{
    public static class CorsExtensions
    {
        public const string DefaultPolicy = "EduOSDefaultCors";

        public static IServiceCollection AddCorsConfiguration(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Read allowed origins from config (or default to localhost for dev)
            var allowedOrigins = configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>()
                ?? new[] { "http://localhost:5000", "https://localhost:5001" };

            services.AddCors(options =>
            {
                options.AddPolicy(DefaultPolicy, builder =>
                {
                    builder
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });

            return services;
        }
    }
}
