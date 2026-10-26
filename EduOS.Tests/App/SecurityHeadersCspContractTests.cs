using EduOS.App.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EduOS.Tests.App;

public sealed class SecurityHeadersCspContractTests
{
    [Fact]
    public async Task Csp_enforces_safe_directives_and_audits_inline_migration_without_blocking_pages()
    {
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        await middleware.InvokeAsync(context);
        Assert.Equal("object-src 'none'; base-uri 'self'; frame-ancestors 'self'",
            context.Response.Headers["Content-Security-Policy"].ToString());
        var audit = context.Response.Headers["Content-Security-Policy-Report-Only"].ToString();
        Assert.Contains("script-src 'self'", audit);
        Assert.Contains("style-src 'self'", audit);
        Assert.DoesNotContain("'unsafe-inline'", audit);
        Assert.DoesNotContain("script-src", context.Response.Headers["Content-Security-Policy"].ToString());
    }
}
