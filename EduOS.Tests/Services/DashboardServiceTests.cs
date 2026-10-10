using EduOS.Core.Entities.Academic;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DashboardServiceTests
{
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
    public async Task Dashboard_rejects_requests_without_a_tenant_context()
    {
        await using var db = new EduOSDbContext(Options());
        var result = await CreateService(db, new TestCurrentUser(0)).GetDashboardAsync();
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    private static DashboardService CreateService(EduOSDbContext db, ICurrentUserService user) => new(
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
        user, NullLogger<DashboardService>.Instance);

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase(
            "dashboard-test-" + Guid.NewGuid().ToString("N")).Options;

    private static ClaimsPrincipal Principal(long tenant) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "99"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.ToString())], "TestAuthentication"));

    private sealed class TestCurrentUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => tenant;
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
