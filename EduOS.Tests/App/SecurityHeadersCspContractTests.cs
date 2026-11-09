using EduOS.App.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using System.Security.Claims;
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

    [Theory]
    [InlineData("/finance/print", true, 200, true)]
    [InlineData("/attendance/corrections/export.csv", true, 403, true)]
    [InlineData("/pricing", false, 200, false)]
    [InlineData("/api/student-documents", false, 401, true)]
    public async Task Actual_http_authenticated_mvc_print_and_export_responses_never_cache_across_tenant_switches(
        string path, bool authenticated, int status, bool noStore)
    {
        using var server = new TestServer(new WebHostBuilder().Configure(app =>
        {
            app.UseSecurityHeaders();
            app.Run(async context =>
            {
                if (context.Request.Headers["X-Test-Authenticated"] == "1")
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "51")], "Test"));
                context.Response.StatusCode = status;
                await context.Response.WriteAsync("test response");
            });
        }));
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (authenticated) request.Headers.Add("X-Test-Authenticated", "1");
        using var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        var cache = response.Headers.CacheControl?.ToString() ?? string.Empty;
        if (noStore) Assert.Contains("no-store", cache, StringComparison.OrdinalIgnoreCase);
        else Assert.DoesNotContain("no-store", cache, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/Dashboard/Index", true, "text/html; charset=utf-8", true)]
    [InlineData("/Account/Login", false, "text/html; charset=utf-8", false)]
    [InlineData("/api/attendance", true, "application/json", false)]
    [InlineData("/finance/export.csv", true, "text/csv", false)]
    public async Task Microphone_permission_is_limited_to_authenticated_application_html(
        string path, bool authenticated, string contentType, bool permitted)
    {
        using var server = new TestServer(new WebHostBuilder().Configure(app =>
        {
            app.UseSecurityHeaders();
            app.Run(async context =>
            {
                if (context.Request.Headers["X-Test-Authenticated"] == "1")
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "51")], "Test"));
                context.Response.ContentType = contentType;
                await context.Response.WriteAsync("test response");
            });
        }));
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (authenticated) request.Headers.Add("X-Test-Authenticated", "1");
        using var response = await client.SendAsync(request);
        var policy = response.Headers.GetValues("Permissions-Policy").Single();
        if (permitted)
        {
            Assert.Contains("microphone=(self)", policy, StringComparison.Ordinal);
            Assert.Contains("on-device-speech-recognition=(self)", policy, StringComparison.Ordinal);
        }
        else
            Assert.Contains("microphone=()", policy, StringComparison.Ordinal);
    }
}
