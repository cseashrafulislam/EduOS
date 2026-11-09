using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
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
    public async Task Dashboard_outstanding_excludes_drafts_deleted_paid_and_other_tenants()
    {
        var http = new DefaultHttpContext();
        await using var db = new EduOSDbContext(Options(),
            new HttpContextAccessor { HttpContext = http });
        db.Tenants.AddRange(
            new Tenant { Id = 41, Name = "Finance dashboard", Code = "DASH-FIN-41", Email = "finance41@example.test" },
            new Tenant { Id = 42, Name = "Other school", Code = "DASH-FIN-42", Email = "finance42@example.test" });
        db.Users.Add(new ApplicationUser { Id = 99, UserName = "finance-admin" });
        db.TenantMemberships.Add(new TenantMembership { Id = 1, TenantId = 41,
            UserId = 99, Status = MembershipStatus.Active });
        db.Roles.Add(new ApplicationRole { Id = 2, TenantId = 41,
            Name = "TenantAdmin", NormalizedName = "TENANTADMIN" });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 99, RoleId = 2 });
        // Seed mixed-tenant fixtures outside request scope; request-time security stays tenant 41.
        await db.SaveChangesAsync();
        var invoices = new[]
        {
            new StudentInvoice { TenantId = 41, InvoiceNumber = "FIN-ISSUED", State = InvoiceState.Issued, DueAmount = 800m },
            new StudentInvoice { TenantId = 41, InvoiceNumber = "FIN-PARTIAL", State = InvoiceState.PartiallyPaid, DueAmount = 150m },
            new StudentInvoice { TenantId = 41, InvoiceNumber = "FIN-DRAFT", State = InvoiceState.Draft, DueAmount = 300m },
            new StudentInvoice { TenantId = 41, InvoiceNumber = "FIN-PAID", State = InvoiceState.Paid, DueAmount = 10m },
            new StudentInvoice { TenantId = 41, InvoiceNumber = "FIN-CANCELLED", State = InvoiceState.Cancelled, DueAmount = 200m },
            new StudentInvoice { TenantId = 41, InvoiceNumber = "FIN-REFUNDED", State = InvoiceState.Refunded, DueAmount = 50m },
            new StudentInvoice { TenantId = 41, InvoiceNumber = "FIN-DELETED", State = InvoiceState.Issued, DueAmount = 900m, IsDeleted = true },
            new StudentInvoice { TenantId = 41, InvoiceNumber = "FIN-USD", State = InvoiceState.Issued, DueAmount = 700m, CurrencyCode = "USD" },
            new StudentInvoice { TenantId = 42, InvoiceNumber = "FIN-OTHER-TENANT", State = InvoiceState.Issued, DueAmount = 1200m }
        };
        db.StudentInvoices.AddRange(invoices);
        await db.SaveChangesAsync();
        http.Items["TenantId"] = 41L;
        var result = await CreateService(db, new TestCurrentUser(41), liveSecurity: true).GetDashboardAsync();
        result.StatusCode.Should().Be(200);
        result.Data!.FinancialSummaryAvailable.Should().BeTrue();
        result.Data.TotalDues.Should().Be(950m);
    }

    [Fact]
    public async Task Principal_operations_are_tenant_and_campus_scoped_and_revoked_roles_fail_closed()
    {
        var http = new DefaultHttpContext();
        await using var db = new EduOSDbContext(Options(), new HttpContextAccessor { HttpContext = http });
        http.Items["TenantId"] = 41L;
        db.Tenants.AddRange(
            new Tenant { Id = 41, Name = "Campus 41", Code = "DASH-OPS-41", Email = "ops41@example.test" },
            new Tenant { Id = 42, Name = "Campus 42", Code = "DASH-OPS-42", Email = "ops42@example.test" });
        db.Users.Add(new ApplicationUser { Id = 99, UserName = "principal" });
        var membership = new TenantMembership { Id = 11, TenantId = 41, UserId = 99,
            Status = MembershipStatus.Active };
        db.TenantMemberships.Add(membership);
        db.Roles.Add(new ApplicationRole { Id = 12, TenantId = 41,
            Name = "Principal", NormalizedName = "PRINCIPAL" });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 99, RoleId = 12 });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 13, TenantId = 41, UserId = 99,
            CampusId = 8, IsActive = true });
        db.AdmissionIntakeForms.AddRange(
            new AdmissionIntakeForm { Id = 51, TenantId = 41, CampusId = 8 },
            new AdmissionIntakeForm { Id = 52, TenantId = 41, CampusId = 9 },
            new AdmissionIntakeForm { Id = 53, TenantId = 42, CampusId = 9 });
        db.AcademicBatches.AddRange(
            new AcademicBatch { Id = 61, TenantId = 41, CampusId = 8 },
            new AcademicBatch { Id = 62, TenantId = 41, CampusId = 9 },
            new AcademicBatch { Id = 63, TenantId = 42, CampusId = 9 });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.AdmissionApplicants.AddRange(
            new AdmissionApplicant { TenantId = 41, AdmissionIntakeFormId = 51,
                FullName = "My pending", ApplicationNumber = "OP-001", State = AdmissionApplicantState.Submitted },
            new AdmissionApplicant { TenantId = 41, AdmissionIntakeFormId = 52,
                FullName = "Other campus", ApplicationNumber = "OP-002", State = AdmissionApplicantState.Submitted },
            new AdmissionApplicant { TenantId = 42, AdmissionIntakeFormId = 53,
                FullName = "Other tenant", ApplicationNumber = "OP-003", State = AdmissionApplicantState.Submitted });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { TenantId = 41, AcademicBatchId = 61, AttendanceDate = today, IsFinalized = false },
            new AttendanceSession { TenantId = 41, AcademicBatchId = 61, AttendanceDate = today, IsFinalized = true },
            new AttendanceSession { TenantId = 41, AcademicBatchId = 62, AttendanceDate = today, IsFinalized = false },
            new AttendanceSession { TenantId = 42, AcademicBatchId = 63, AttendanceDate = today, IsFinalized = false });
        // Seed two tenants as an unauthenticated system fixture. Production
        // tenant-boundary validation must remain enabled for request writes.
        http.Items.Remove("TenantId");
        await db.SaveChangesAsync();
        http.Items["TenantId"] = 41L;
        var service = CreateService(db, new TestCurrentUser(41, admin: false, role: "Principal"), liveSecurity: true);
        var visible = (await service.GetDashboardAsync()).Data!;
        visible.AdmissionApplicationsToReview.Should().Be(1);
        visible.AttendanceSessionsToday.Should().Be(2);
        visible.AttendanceSessionsAwaitingFinalization.Should().Be(1);
        visible.OperationalDate.Should().Be(today);
        visible.MonthlyCollection.Should().BeNull();
        visible.FinancialSummaryAvailable.Should().BeFalse();

        membership.Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        var revoked = (await service.GetDashboardAsync()).Data!;
        revoked.AdmissionApplicationsToReview.Should().BeNull();
        revoked.AttendanceSessionsToday.Should().BeNull();
        revoked.OperationalDate.Should().BeNull();
        revoked.TotalDues.Should().BeNull();
    }

    [Fact]
    public async Task Ordinary_student_receives_no_protected_daily_work_counts()
    {
        var http = new DefaultHttpContext();
        await using var db = new EduOSDbContext(Options(), new HttpContextAccessor { HttpContext = http });
        http.Items["TenantId"] = 41L;
        db.Tenants.Add(new Tenant { Id = 41, Name = "Student school", Code = "DASH-ST-41",
            Email = "student41@example.test" });
        await db.SaveChangesAsync();
        var student = (await CreateService(db, new TestCurrentUser(41, admin: false), liveSecurity: true)
            .GetDashboardAsync()).Data!;
        student.OperationalDate.Should().BeNull();
        student.AdmissionApplicationsToReview.Should().BeNull();
        student.AttendanceSessionsToday.Should().BeNull();
        student.MyAttendanceSessionsToday.Should().BeNull();
        student.FinancialSummaryAvailable.Should().BeFalse();
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

    private sealed class TestCurrentUser(long tenant, bool admin = true, string? role = null) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => tenant;
        public string? FullName => "Tenant Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => [role ?? (admin ? "TenantAdmin" : "Student")];
        public bool IsInRole(string requested) => Roles.Contains(requested, StringComparer.OrdinalIgnoreCase);
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
