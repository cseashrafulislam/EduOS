using System.Security.Claims;
using EduOS.App.Middleware;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            State = state, OnboardingStage = stage,
            OnboardingCompletedAt = stage == OnboardingStage.Completed ? DateTime.UtcNow : null };
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
    public async Task Verified_active_subscription_unlocks_dashboard_but_setup_stays_incomplete()
    {
        using var db = CreateContext();
        var tenant = new Tenant
        {
            Name = "Paid School", Code = "PAID", Email = "paid@example.test",
            InstitutionTypeDefinitionId = 1, EmailVerifiedAt = DateTime.UtcNow.AddDays(-1),
            State = TenantState.Active, OnboardingStage = OnboardingStage.CampusSetup
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id, SubscriptionPlanId = 1, State = SubscriptionState.Active,
            StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(28)
        });
        await db.SaveChangesAsync();
        var http = CreateRequest(tenant.Id, "/Dashboard/Index");
        var next = false;
        var guard = new OnboardingGuardMiddleware(_ => { next = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);
        await guard.InvokeAsync(http, db);
        Assert.True(next);
        Assert.Null(tenant.OnboardingCompletedAt);
        Assert.Equal(OnboardingStage.CampusSetup, tenant.OnboardingStage);
    }

    [Fact]
    public async Task Expired_subscription_and_missing_email_verification_never_unlock_dashboard()
    {
        using var db = CreateContext();
        var tenant = new Tenant
        {
            Name = "School", Code = "EXPIRED", Email = "expired@example.test",
            InstitutionTypeDefinitionId = 1, EmailVerifiedAt = DateTime.UtcNow.AddDays(-2),
            State = TenantState.Active, OnboardingStage = OnboardingStage.CampusSetup
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id, SubscriptionPlanId = 1, State = SubscriptionState.Active,
            StartsAt = DateTime.UtcNow.AddDays(-30), EndsAt = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var guard = new OnboardingGuardMiddleware(_ => Task.CompletedTask,
            NullLogger<OnboardingGuardMiddleware>.Instance);
        var expired = CreateRequest(tenant.Id, "/Dashboard/Index");
        await guard.InvokeAsync(expired, db);
        Assert.Equal(StatusCodes.Status302Found, expired.Response.StatusCode);
        var subscription = await db.TenantSubscriptions.IgnoreQueryFilters().SingleAsync();
        subscription.EndsAt = DateTime.UtcNow.AddDays(1);
        tenant.EmailVerifiedAt = null;
        await db.SaveChangesAsync();
        var unverified = CreateRequest(tenant.Id, "/Dashboard/Index");
        await guard.InvokeAsync(unverified, db);
        Assert.Equal(StatusCodes.Status302Found, unverified.Response.StatusCode);
    }

    [Fact]
    public async Task Changing_language_is_allowed_while_other_onboarding_pages_remain_guarded()
    {
        using var db = CreateContext();
        var tenant = new Tenant
        {
            Name = "Onboarding", Code = "LANG", Email = "lang@example.test",
            State = TenantState.PendingVerification, OnboardingStage = OnboardingStage.Payment
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var request = CreateRequest(tenant.Id, "/localization/set-language");
        request.Request.Method = HttpMethods.Post;
        var reached = false;
        var guard = new OnboardingGuardMiddleware(_ => { reached = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);
        await guard.InvokeAsync(request, db);
        Assert.True(reached);
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
    public async Task Completed_stage_without_persisted_timestamp_is_rejected()
    {
        using var db = CreateContext();
        var tenant = new Tenant { Name = "Test", Code = "TST", Email = "test@example.test",
            State = TenantState.Active, OnboardingStage = OnboardingStage.Completed };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var http = CreateRequest(tenant.Id, "/Students/Index");
        var next = false;
        var middleware = new OnboardingGuardMiddleware(_ => { next = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);

        await middleware.InvokeAsync(http, db);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
    }

    [Fact]
    public async Task Incomplete_tenant_api_exposes_only_required_onboarding_step()
    {
        using var db = CreateContext();
        var tenant = new Tenant { Name = "Test", Code = "TST", Email = "test@example.test",
            State = TenantState.Active, OnboardingStage = OnboardingStage.CampusSetup };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var http = CreateRequest(tenant.Id, "/api/students");
        var next = false;
        var middleware = new OnboardingGuardMiddleware(_ => { next = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);

        await middleware.InvokeAsync(http, db);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.Equal("true", http.Response.Headers["X-Onboarding-Required"].ToString());
        Assert.Equal(((int)OnboardingStage.CampusSetup).ToString(), http.Response.Headers["X-Onboarding-Step"].ToString());
    }

    [Theory]
    [InlineData("/api/platform-catalog/institution-types")]
    [InlineData("/api/platform-catalog/modules")]
    [InlineData("/api/platform-catalog/institution-types/SECONDARY_SCHOOL")]
    public async Task Public_platform_catalog_get_remains_available_during_onboarding(string path)
    {
        using var db = CreateContext();
        var http = CreateRequest(null, path);
        http.Request.Method = HttpMethods.Get;
        var next = false;
        var middleware = new OnboardingGuardMiddleware(_ => { next = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);

        await middleware.InvokeAsync(http, db);

        Assert.True(next);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
    }

    [Fact]
    public async Task Public_platform_catalog_write_is_not_granted_an_onboarding_bypass()
    {
        using var db = CreateContext();
        var http = CreateRequest(null, "/api/platform-catalog/institution-types");
        http.Request.Method = HttpMethods.Post;
        var next = false;
        var middleware = new OnboardingGuardMiddleware(_ => { next = true; return Task.CompletedTask; },
            NullLogger<OnboardingGuardMiddleware>.Instance);

        await middleware.InvokeAsync(http, db);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
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
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        http.Request.Path = path;
        if (tenantId.HasValue) http.Items["TenantId"] = tenantId.Value;
        return http;
    }
}
