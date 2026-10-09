using System.Security.Claims;
using EduOS.App.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EduOS.Tests.App;

public sealed class PrivilegedMfaMiddlewareTests
{
    [Theory]
    [InlineData("/Dashboard/Index", "TenantAdmin")]
    [InlineData("/hangfire", "SuperAdmin")]
    [InlineData("/Account/Profile", "AdmissionOfficer")]
    public async Task Password_only_privileged_session_is_redirected(string path, string role)
    {
        var (context, nextCalled) = await InvokeAsync(path, role, false);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/Account/MfaSetup", context.Response.Headers.Location.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("/api/subscription", "TenantAdmin")]
    [InlineData("/api/tenant-settings", "SuperAdmin")]
    public async Task Password_only_privileged_api_session_is_denied(string path, string role)
    {
        var (context, nextCalled) = await InvokeAsync(path, role, false);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("/Account/MfaSetup", "TenantAdmin", false)]
    [InlineData("/api/auth/mfa/setup", "TenantAdmin", false)]
    [InlineData("/api/auth/mfa/enable", "AdmissionOfficer", false)]
    [InlineData("/Dashboard/Index", "TenantAdmin", true)]
    [InlineData("/Dashboard/Index", "Student", false)]
    public async Task Mfa_bootstrap_or_permitted_session_reaches_next(string path, string role, bool verified)
    {
        var (_, nextCalled) = await InvokeAsync(path, role, verified);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Unauthenticated_session_reaches_next_for_endpoint_authorization()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/Dashboard/Index";
        var nextCalled = false;
        var middleware = new PrivilegedMfaMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        Assert.True(nextCalled);
    }

    private static async Task<(DefaultHttpContext Context, bool NextCalled)> InvokeAsync(string path, string role, bool verified)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "123"), new(ClaimTypes.Role, role) };
        if (verified) claims.Add(new Claim("amr", "mfa"));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestCookie"));
        var nextCalled = false;
        var middleware = new PrivilegedMfaMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        return (context, nextCalled);
    }
}
