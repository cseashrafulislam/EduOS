using System.Security.Claims;
using EduOS.App.Middleware;
using EduOS.App.Security;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

public sealed class AuthSessionRedirectLoopTests
{
    [Theory]
    [InlineData("/Account/Login", "GET")]
    [InlineData("/", "GET")]
    [InlineData("/api/auth/login", "POST")]
    [InlineData("/Account/MfaChallenge", "GET")]
    public async Task Invalid_cookie_allows_explicit_anonymous_auth_entry_only(string path, string method)
    {
        await using var db = Db();
        var request = Request(path, method, true);
        var reached = false;
        var middleware = new AuthSessionMiddleware(context =>
        {
            reached = true;
            Assert.False(context.User.Identity?.IsAuthenticated == true);
            Assert.Empty(context.User.Claims);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(request, db);

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, request.Response.StatusCode);
        Assert.False(request.Response.Headers.ContainsKey("Location"));
        Assert.False(request.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal("no-store", request.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Revoked_registry_cookie_can_reauthenticate_without_clearing_a_newer_cookie()
    {
        await using var db = Db();
        var sid = await AuthSessionRegistry.CreateAsync(db, 51, 25, "", "", false);
        Assert.True(await AuthSessionRegistry.RevokeAsync(db, 51, sid));
        var request = Request("/Account/Login", "GET", true, sid);
        var reached = false;
        var middleware = new AuthSessionMiddleware(context =>
        {
            reached = true;
            Assert.False(context.User.Identity?.IsAuthenticated == true);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(request, db);

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, request.Response.StatusCode);
        Assert.False(request.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.False(request.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Malformed_tenant_claim_does_not_block_anonymous_login()
    {
        await using var db = Db();
        var request = Request("/Account/Login", "GET", true, tenantClaim: "wrong");
        var reached = false;
        var middleware = new AuthSessionMiddleware(context =>
        {
            reached = true;
            Assert.False(context.User.Identity?.IsAuthenticated == true);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(request, db);

        Assert.True(reached);
        Assert.False(request.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Theory]
    [InlineData("/Dashboard/Index", "GET", 302, "/Account/Login")]
    [InlineData("/api/students", "GET", 401, null)]
    [InlineData("/api/auth/login", "POST", 401, null)]
    public async Task Invalid_cookie_rejects_protected_and_unmarked_endpoints(
        string path, string method, int expectedStatus, string? redirect)
    {
        await using var db = Db();
        var request = Request(path, method, false);
        var reached = false;
        var middleware = new AuthSessionMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(request, db);

        Assert.False(reached);
        Assert.Equal(expectedStatus, request.Response.StatusCode);
        Assert.False(request.Response.Headers.ContainsKey("Set-Cookie"));
        if (redirect != null) Assert.Equal(redirect, request.Response.Headers.Location.ToString());
        else Assert.False(request.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Live_session_still_reaches_authorized_pipeline()
    {
        await using var db = Db();
        var sid = await AuthSessionRegistry.CreateAsync(db, 51, 25, "", "", false);
        var request = Request("/Dashboard/Index", "GET", false, sid);
        var reached = false;
        var middleware = new AuthSessionMiddleware(context =>
        {
            reached = true;
            Assert.True(context.User.Identity?.IsAuthenticated == true);
            Assert.Equal("25", context.User.FindFirstValue("TenantId"));
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(request, db);

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, request.Response.StatusCode);
    }

    private static EduOSDbContext Db() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("auth-session-redirect-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static DefaultHttpContext Request(string path, string method, bool allowAnonymous,
        string? sessionId = null, string tenantClaim = "25")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "51"),
            new("TenantId", tenantClaim),
            new(ClaimTypes.Role, "TenantAdmin")
        };
        if (sessionId != null) claims.Add(new Claim(AuthSessionRegistry.ClaimType, sessionId));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestCookie"))
        };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        if (allowAnonymous)
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(new AllowAnonymousAttribute()), "Anonymous login endpoint"));
        return context;
    }
}
