using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Repositories.SaaS;
using EduOS.Persistence.Seed;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class DashboardServiceTests
{
    [Fact]
    public async Task Dashboard_exposes_bilingual_plan_and_stable_localization_alert_codes()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"dashboard-{Guid.NewGuid():N}")
            .Options;
        await using var context = new EduOSDbContext(options, accessor);

        await SubscriptionSeeder.SeedAsync(context);
        var tenant = new Tenant
        {
            Name = "বাংলা টেস্ট স্কুল",
            Code = $"DASH-{Guid.NewGuid():N}"[..20],
            Email = "school@example.test",
            OwnerName = "Test Owner",
            IsEmailVerified = false,
            IsOnboardingComplete = false,
            OnboardingStep = OnboardingStep.Payment,
            IsTrialActive = true,
            CurrentStudents = 90,
            MaxStudents = 100,
            CurrentTeachers = 12,
            MaxTeachers = 20,
            MaxCampuses = 2
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        httpContext.Items["TenantId"] = tenant.Id;
        httpContext.User = TenantAdminPrincipal(tenant.Id);

        var plan = await context.SubscriptionPlans.SingleAsync(x => x.Code == "TRIAL");
        context.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            TrialStartDate = DateTime.UtcNow.AddDays(-1),
            TrialEndDate = DateTime.UtcNow.AddDays(2),
            IsTrial = true,
            Status = SubscriptionStatus.Trialing,
            Currency = "BDT"
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new TestCurrentUser(tenant.Id));
        var result = await service.GetDashboardAsync();

        result.Success.Should().BeTrue();
        result.Data!.PlanNameBangla.Should().Be("ফ্রি ট্রায়াল");
        result.Data.ActiveFeatures.Should().BeGreaterThan(0);
        result.Data.Alerts.Select(x => x.Code).Should().Contain(
        [
            "EMAIL_UNVERIFIED",
            "ONBOARDING_INCOMPLETE",
            "TRIAL_EXPIRING",
            "STUDENT_LIMIT_WARNING"
        ]);
        result.Data.Alerts.Single(x => x.Code == "TRIAL_EXPIRING")
            .Days!.Value.Should().BeInRange(1, 2);
        result.Data.Alerts.Single(x => x.Code == "STUDENT_LIMIT_WARNING")
            .CurrentValue.Should().Be(90);
    }

    [Fact]
    public async Task Dashboard_rejects_requests_without_a_tenant_context()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"dashboard-no-tenant-{Guid.NewGuid():N}")
            .Options;
        await using var context = new EduOSDbContext(options);
        var service = CreateService(context, new TestCurrentUser(0));

        var result = await service.GetDashboardAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        result.Data.Should().BeNull();
    }

    private static DashboardService CreateService(
        EduOSDbContext context,
        ICurrentUserService currentUser) =>
        new(
            new GenericRepository<Tenant>(context),
            currentUser,
            NullLogger<DashboardService>.Instance,
            new SubscriptionPlanRepository(context),
            new TenantSubscriptionRepository(context));

    private static ClaimsPrincipal TenantAdminPrincipal(long tenantId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication"));

    private sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => tenantId;
        public string? FullName => "Tenant Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
