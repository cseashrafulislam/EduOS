using System.Net;
using System.Text.Json;

namespace EduOS.App.Middlewares
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IWebHostEnvironment _environment;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IWebHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access. Path: {Path}", context.Request.Path);
                await HandleExceptionAsync(context, HttpStatusCode.Unauthorized, ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Resource not found. Path: {Path}", context.Request.Path);
                await HandleExceptionAsync(context, HttpStatusCode.NotFound, ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Bad request. Path: {Path}", context.Request.Path);
                await HandleExceptionAsync(context, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception. Path: {Path}", context.Request.Path);

                var message = _environment.IsDevelopment()
                    ? ex.Message
                    : "An unexpected error occurred.";

                await HandleExceptionAsync(context, HttpStatusCode.InternalServerError, message);
            }
        }

        private static async Task HandleExceptionAsync(
            HttpContext context,
            HttpStatusCode statusCode,
            string message)
        {
            if (context.Response.HasStarted)
                return;

            context.Response.Clear();
            context.Response.StatusCode = (int)statusCode;

            var isApiRequest =
                context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Headers["Accept"].Any(x => x.Contains("application/json", StringComparison.OrdinalIgnoreCase));

            if (isApiRequest)
            {
                context.Response.ContentType = "application/json";

                var response = new
                {
                    success = false,
                    statusCode = (int)statusCode,
                    message
                };

                var json = JsonSerializer.Serialize(response);
                await context.Response.WriteAsync(json);
                return;
            }

            if (statusCode == HttpStatusCode.Forbidden)
            {
                context.Response.Redirect("/Error/403");
                return;
            }

            if (statusCode == HttpStatusCode.NotFound)
            {
                context.Response.Redirect("/Error/404");
                return;
            }

            if (statusCode == HttpStatusCode.Unauthorized)
            {
                context.Response.Redirect("/Account/Login");
                return;
            }

            context.Response.Redirect("/Error/500");
        }
    }
}