using System.Security.Claims;
using EduOS.App.Middleware;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed class OnboardingGuardCanonicalStateTests
{
    [Theory]
    [InlineData(TenantState.Active, OnboardingStage.Completed, true, 200)]
    [InlineData(TenantState.Active, OnboardingStage.InstitutionProfile, false, 302)]
    [InlineData(TenantState.PendingVerification, OnboardingStage.InstitutionProfile, false, 302)]
    [InlineData(TenantState.PendingVerification, OnboardingStage.Completed, false, 403)]
    [InlineData(TenantState.Suspended, OnboardingStage.Completed, false, 403)]
    [InlineData(TenantState.Closed, OnboardingStage.Completed, false, 403)]
    public async Task Canonical_tenant_state_controls_protected_route(
        TenantState state, OnboardingStage stage, bool expectedNext, int expectedStatus)
    {
        using var db = CreateContext();
        var tenant = new Tenant { Name = "Test", Code = "TST", Email = "test@example.test",
            State = state, OnboardingStage = stage };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var http = CreateRequest(tenant.Id, "/Students/Index");
        var next = false;
        var middleware = new OnboardingGuardMiddleware(_ => { next = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);

        await middleware.InvokeAsync(http, db);

        Assert.Equal(expectedNext, next);
        Assert.Equal(expectedStatus, http.Response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_is_not_an_onboarding_bypass()
    {
        using var db = CreateContext();
        var tenant = new Tenant { Name = "Test", Code = "TST", Email = "test@example.test",
            State = TenantState.Active, OnboardingStage = OnboardingStage.CampusSetup };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var http = CreateRequest(tenant.Id, "/Dashboard/Index");
        var next = false;
        var middleware = new OnboardingGuardMiddleware(_ => { next = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);

        await middleware.InvokeAsync(http, db);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status302Found, http.Response.StatusCode);
        Assert.Equal("/Account/CampusSetup", http.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Missing_trusted_tenant_context_is_rejected()
    {
        using var db = CreateContext();
        var http = CreateRequest(null, "/Students/Index");
        var next = false;
        var middleware = new OnboardingGuardMiddleware(_ => { next = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);

        await middleware.InvokeAsync(http, db);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
    }

    private static EduOSDbContext CreateContext() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"onboarding-guard-{Guid.NewGuid():N}").Options);

    private static DefaultHttpContext CreateRequest(long? tenantId, string path)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "51"),
                new Claim(ClaimTypes.Role, "TenantAdmin")
            ], "TestCookie")),
            Response = { Body = new MemoryStream() }
        };
        http.Request.Path = path;
        if (tenantId.HasValue) http.Items["TenantId"] = tenantId.Value;
        return http;
    }
}
