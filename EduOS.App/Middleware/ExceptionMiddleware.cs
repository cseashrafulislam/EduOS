using System.Net;
using System.Text.Json;

namespace EduOS.App.Middleware
{
    /// <summary>
    /// Global exception handler middleware.
    /// Catches all unhandled exceptions and returns proper JSON response.
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IWebHostEnvironment _env;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IWebHostEnvironment env)
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
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled exception. Path: {Path}, User: {UserId}, TraceId: {TraceId}",
                    context.Request.Path,
                    context.User?.Identity?.Name ?? "Anonymous",
                    context.TraceIdentifier);

                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            context.Response.ContentType = "application/json";

            var (statusCode, message) = ex switch
            {
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized access"),
                KeyNotFoundException => (HttpStatusCode.NotFound, ex.Message),
                ArgumentException => (HttpStatusCode.BadRequest, ex.Message),
                InvalidOperationException => (HttpStatusCode.BadRequest, ex.Message),
                NotImplementedException => (HttpStatusCode.NotImplemented, "Feature not implemented"),
                _ => (HttpStatusCode.InternalServerError, "An error occurred while processing your request")
            };

            context.Response.StatusCode = (int)statusCode;

            var response = new
            {
                success = false,
                message,
                statusCode = (int)statusCode,
                traceId = context.TraceIdentifier,
                // Include stack trace only in development
                detail = _env.IsDevelopment() ? ex.ToString() : null
            };

            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}