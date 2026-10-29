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

    [Theory]
    [InlineData("/api/students", "no-store")]
    [InlineData("/api/subscription/current", "no-store")]
    [InlineData("/api/public/admissions/school", "no-store")]
    [InlineData("/images/logo.png", "")]
    public async Task Api_payloads_are_not_browser_or_shared_proxy_cacheable(
        string requestPath, string expectedCacheControl)
    {
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Path = requestPath;
        await middleware.InvokeAsync(context);

        Assert.Equal(expectedCacheControl, context.Response.Headers.CacheControl.ToString());
        if (expectedCacheControl.Length > 0)
            Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
    }
}
