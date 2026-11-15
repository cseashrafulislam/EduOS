using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace EduOS.App.Middleware;

// Enabled only for the private Codespaces SQL demo via Program.cs. Never logs
// request bodies, cookies, antiforgery tokens, user identities or database values.
public static class QaAcademicWriteTraceExtensions
{
    public static IApplicationBuilder UseQaAcademicWriteTrace(this IApplicationBuilder app, string stage)
    {
        return app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPost(context.Request.Method) ||
                !string.Equals(context.Request.Path.Value, "/api/academic-setup/programmes",
                    StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("EduOS.Staging.AcademicProgrammeQa");
            var clock = Stopwatch.StartNew();
            logger.LogWarning("QA programme POST {Stage}: enter ({TraceId})", stage, context.TraceIdentifier);
            try
            {
                await next(context);
                logger.LogWarning("QA programme POST {Stage}: exit HTTP {Status} after {ElapsedMs}ms ({TraceId})",
                    stage, context.Response.StatusCode, clock.ElapsedMilliseconds, context.TraceIdentifier);
            }
            catch (Exception error)
            {
                logger.LogError(error, "QA programme POST {Stage}: exception after {ElapsedMs}ms ({TraceId})",
                    stage, clock.ElapsedMilliseconds, context.TraceIdentifier);
                throw;
            }
        });
    }
}
