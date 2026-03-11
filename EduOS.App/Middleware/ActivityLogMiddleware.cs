using Microsoft.AspNetCore.Http;

namespace EduOS.App.Middleware
{
    public class ActivityLogMiddleware
    {
        private readonly RequestDelegate _next;

        public ActivityLogMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            await _next(context);
        }
    }
}
