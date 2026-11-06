using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Seed;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Ordinary_members_never_receive_institution_financial_values()
    {
        var http = new DefaultHttpContext();
        await using var db = new EduOSDbContext(Options(),
            new HttpContextAccessor { HttpContext = http });
        db.Tenants.Add(new Tenant { Id = 41, Name = "Finance privacy school",
            Code = "DASH-SEC-41", Email = "sec41@example.test" });
        http.Items["TenantId"] = 41L;
        await db.SaveChangesAsync();
        var result = await CreateService(db, new TestCurrentUser(41, admin: false))
            .GetDashboardAsync();
        result.StatusCode.Should().Be(200);
        result.Data!.FinancialSummaryAvailable.Should().BeFalse();
        result.Data.MonthlyCollection.Should().BeNull();
        result.Data.TotalDues.Should().BeNull();
    }

    [Fact]
    public async Task Dashboard_finance_requires_current_live_administrator_role_and_membership()
    {
        var http = new DefaultHttpContext();
        await using var db = new EduOSDbContext(Options(),
            new HttpContextAccessor { HttpContext = http });
        db.Tenants.Add(new Tenant { Id = 41, Name = "Admin school",
            Code = "DASH-SEC-ADMIN", Email = "secadmin@example.test" });
        http.Items["TenantId"] = 41L;
        db.Users.Add(new ApplicationUser { Id = 99, UserName = "admin" });
        db.TenantMemberships.Add(new TenantMembership { Id = 1, TenantId = 41,
            UserId = 99, Status = MembershipStatus.Active });
        db.Roles.Add(new ApplicationRole { Id = 2, TenantId = 41,
            Name = "TenantAdmin", NormalizedName = "TENANTADMIN" });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 99, RoleId = 2 });
        await db.SaveChangesAsync();

        var service = CreateService(db, new TestCurrentUser(41), liveSecurity: true);
        var authorized = await service.GetDashboardAsync();
        authorized.StatusCode.Should().Be(200);
        authorized.Data!.FinancialSummaryAvailable.Should().BeTrue();
        authorized.Data.MonthlyCollection.Should().Be(0m);
        authorized.Data.TotalDues.Should().Be(0m);
        db.UserRoles.Remove(db.UserRoles.Single());
        await db.SaveChangesAsync();
        var revoked = await service.GetDashboardAsync();
        revoked.StatusCode.Should().Be(200);
        revoked.Data!.FinancialSummaryAvailable.Should().BeFalse();
        revoked.Data.MonthlyCollection.Should().BeNull();
        revoked.Data.TotalDues.Should().BeNull();
    }
    [Fact]
    public async Task Dashboard_derives_plan_capacity_and_alerts_from_authoritative_records()
    {
        var http = new DefaultHttpContext();
        var options = Options();
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        await SubscriptionSeeder.SeedAsync(db);
        var trial = await db.SubscriptionPlans.SingleAsync(x => x.Code == "TRIAL");
        trial.MaxStudents = 100;
        var tenant = new Tenant { Name = "Test School", Code = "DASH-101",
            Email = "school@example.test", OnboardingStage = OnboardingStage.Payment };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        http.Items["TenantId"] = tenant.Id;
        http.User = Principal(tenant.Id);
        var now = DateTime.UtcNow;
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id, SubscriptionPlanId = trial.Id,
            StartsAt = now.AddDays(-1), EndsAt = now.AddDays(2),
            State = SubscriptionState.Trial, IsTrial = true, CurrencyCode = "BDT"
        });
        for (var i = 0; i < 90; i++)
            db.Students.Add(new Student
            {
                TenantId = tenant.Id, PublicId = Guid.NewGuid(),
                StudentCode = "STU-" + i, FullName = "Student " + i,
                StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1)
            });
        await db.SaveChangesAsync();
        var result = await CreateService(db, new TestCurrentUser(tenant.Id)).GetDashboardAsync();
        result.Success.Should().BeTrue(result.Message);
        result.Data!.PlanName.Should().Be("Free Trial");
        result.Data.OnboardingStage.Should().Be(OnboardingStage.Payment);
        result.Data.OnboardingPercent.Should().Be(30);
        result.Data.CurrentStudents.Should().Be(90);
        result.Data.Alerts.Select(x => x.Code)
            .Should().Contain(["EMAIL_UNVERIFIED", "ONBOARDING_INCOMPLETE",
                "TRIAL_EXPIRING", "STUDENT_LIMIT_WARNING"]);
        result.Data.Alerts.Single(x => x.Code == "STUDENT_LIMIT_WARNING")
            .CurrentValue.Should().Be(90);
    }

    [Fact]
    public async Task Dashboard_recognizes_active_paid_subscription_for_current_tenant()
    {
        var http = new DefaultHttpContext();
        await using var db = new EduOSDbContext(Options(), new HttpContextAccessor { HttpContext = http });
        await SubscriptionSeeder.SeedAsync(db);
        var enterprise = await db.SubscriptionPlans.SingleAsync(x => x.Code == "ENTERPRISE");
        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Name = "Paid School", Code = "DASH-PAID",
            Email = "paid@example.test", State = TenantState.Active,
            EmailVerifiedAt = now, OnboardingStage = OnboardingStage.Completed,
            OnboardingCompletedAt = now
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        http.Items["TenantId"] = tenant.Id;
        http.User = Principal(tenant.Id);
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id, SubscriptionPlanId = enterprise.Id,
            StartsAt = now.AddHours(-1), EndsAt = now.AddMonths(1),
            State = SubscriptionState.Active, IsTrial = false,
            BillingCycleCode = "Monthly", PriceSnapshot = enterprise.MonthlyPrice, CurrencyCode = "BDT"
        });
        await db.SaveChangesAsync();
        var result = await CreateService(db, new TestCurrentUser(tenant.Id)).GetDashboardAsync();
        result.Success.Should().BeTrue(result.Message);
        result.Data!.PlanName.Should().Be("Enterprise");
        result.Data.SubscriptionStatus.Should().Be("Active");
        result.Data.SubscriptionEndDate.Should().NotBeNull();
        result.Data.Alerts.Select(x => x.Code).Should().NotContain("SUBSCRIPTION_MISSING");
    }

    [Fact]
    public async Task Dashboard_rejects_requests_without_a_tenant_context()
    {
        await using var db = new EduOSDbContext(Options());
        var result = await CreateService(db, new TestCurrentUser(0)).GetDashboardAsync();
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    private static DashboardService CreateService(EduOSDbContext db, ICurrentUserService user,
        bool liveSecurity = false) => new(
        new GenericRepository<Tenant>(db),
        new GenericRepository<InstitutionTypeDefinition>(db),
        new GenericRepository<SubscriptionPlan>(db),
        new GenericRepository<PlanFeature>(db),
        new EduOS.Persistence.Repositories.SaaS.TenantSubscriptionRepository(db),
        new GenericRepository<Student>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<Campus>(db),
        new GenericRepository<AcademicLevel>(db),
        new GenericRepository<StudentPayment>(db),
        new GenericRepository<StudentInvoice>(db),
        user, NullLogger<DashboardService>.Instance,
        securityDb: liveSecurity ? db : null);

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase(
            "dashboard-test-" + Guid.NewGuid().ToString("N")).Options;

    private static ClaimsPrincipal Principal(long tenant) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "99"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.ToString())], "TestAuthentication"));

    private sealed class TestCurrentUser(long tenant, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => tenant;
        public string? FullName => "Tenant Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Student"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Student";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
