using EduOS.Core.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace EduOS.App.Middleware
{
    /// <summary>
    /// Catches unhandled exceptions and returns a consistent error response.
    /// For API requests, returns JSON. For MVC requests, redirects to error page.
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _env;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access on {Path}", context.Request.Path);
                await HandleAsync(context, HttpStatusCode.Unauthorized,
                    "You are not authorized to perform this action");
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Resource not found on {Path}", context.Request.Path);
                await HandleAsync(context, HttpStatusCode.NotFound,
                    ex.Message ?? "Resource not found");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid argument on {Path}", context.Request.Path);
                await HandleAsync(context, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation on {Path}", context.Request.Path);
                await HandleAsync(context, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

                var message = _env.IsDevelopment()
                    ? ex.Message
                    : "An unexpected error occurred. Please try again later.";

                await HandleAsync(context, HttpStatusCode.InternalServerError, message);
            }
        }

        private async Task HandleAsync(HttpContext context, HttpStatusCode statusCode, string message)
        {
            // Don't try to write if response already started
            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Cannot write error response - response already started");
                return;
            }

            context.Response.Clear();
            context.Response.StatusCode = (int)statusCode;

            var isApi = context.Request.Path.StartsWithSegments("/api") ||
                        context.Request.Headers.Accept.ToString().Contains("application/json");

            if (isApi)
            {
                context.Response.ContentType = "application/json";

                var response = new ApiResponse<object>
                {
                    Success = false,
                    Message = message,
                    StatusCode = (int)statusCode
                };

                var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await context.Response.WriteAsync(json);
            }
            else
            {
                // MVC - redirect to error page
                context.Response.Redirect($"/Error/{(int)statusCode}");
            }
        }
    }

    public static class ExceptionMiddlewareExtensions
    {
        public static IApplicationBuilder UseCustomExceptionMiddleware(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ExceptionMiddleware>();
        }
    }
}
