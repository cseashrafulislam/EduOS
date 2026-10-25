using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Repositories.SaaS;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionServiceTests
{
    [Fact]
    public async Task Paid_subscription_creates_one_immutable_invoice_and_advances_to_payment()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        var plan = await AddPlanAsync(setup.Db, 1000m);
        var request = Request(plan.Id);
        var created = await setup.Service.StartAsync(request);
        setup.Db.CurrentTenantId.Should().Be(setup.Tenant.Id, "the request-scoped tenant must remain stable during subscription posting");
        (await setup.Db.TenantSubscriptions.AsNoTracking()
            .CountAsync(x => x.PublicId == request.ClientRequestId)).Should().Be(1,
                "a retry must see the existing subscription by its idempotency key");
        var replay = await setup.Service.StartAsync(request);
        created.Success.Should().BeTrue();
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(created.Data!.Id);
        var invoice = await setup.Db.SubscriptionInvoices.SingleAsync();
        invoice.Subtotal.Should().Be(1000m);
        invoice.TotalAmount.Should().Be(1000m);
        invoice.DueAmount.Should().Be(1000m);
        (await setup.Db.SubscriptionInvoiceLines.SingleAsync()).Amount.Should().Be(1000m);
        setup.Tenant.OnboardingStage.Should().Be(OnboardingStage.Payment);
    }

    [Fact]
    public async Task Trial_subscription_activates_without_invoice()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        var plan = await AddPlanAsync(setup.Db, 500m, trialDays: 14);
        var result = await setup.Service.StartAsync(new StartSubscriptionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), SubscriptionPlanId = plan.Id,
            BillingCycleCode = "Monthly", StartTrialIfEligible = true
        });
        result.Success.Should().BeTrue();
        result.Data!.IsTrial.Should().BeTrue();
        result.Data.State.Should().Be(SubscriptionState.Trial);
        (await setup.Db.SubscriptionInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Nonpublic_plan_is_not_available_and_cannot_create_subscription()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        var plan = await AddPlanAsync(setup.Db, 500m, isPublic: false);
        var result = await setup.Service.StartAsync(Request(plan.Id));
        result.StatusCode.Should().Be(404);
        (await setup.Db.TenantSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Pending_subscription_blocks_second_payment_request()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        var plan = await AddPlanAsync(setup.Db, 500m);
        setup.Db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = setup.Tenant.Id, SubscriptionPlanId = plan.Id,
            State = SubscriptionState.PendingPayment, BillingCycleCode = "Monthly",
            StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddMonths(1),
            CurrencyCode = "BDT", PriceSnapshot = 500m
        });
        await setup.Db.SaveChangesAsync();
        setup.Db.CurrentTenantId.Should().Be(setup.Tenant.Id);
        (await setup.Db.TenantSubscriptions.AsNoTracking()
            .CountAsync(x => x.State == SubscriptionState.PendingPayment)).Should().Be(1,
                "a pending payment must be visible before the new request is processed");
        var result = await setup.Service.StartAsync(Request(plan.Id));
        result.StatusCode.Should().Be(409);
        (await setup.Db.TenantSubscriptions.CountAsync()).Should().Be(1);
        (await setup.Db.SubscriptionInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Tenant_must_verify_email_before_starting_subscription()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        setup.Tenant.EmailVerifiedAt = null;
        await setup.Db.SaveChangesAsync();
        var plan = await AddPlanAsync(setup.Db, 500m);
        (await setup.Service.StartAsync(Request(plan.Id))).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Renewal_creates_one_future_period_and_one_invoice_without_displacing_current()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        var plan = await AddPlanAsync(setup.Db, 1000m);
        (await setup.Service.StartAsync(Request(plan.Id))).Success.Should().BeTrue();
        var current = await setup.Db.TenantSubscriptions.SingleAsync();
        current.State = SubscriptionState.Active;
        current.StartsAt = DateTime.UtcNow.AddDays(-20);
        current.EndsAt = DateTime.UtcNow.AddDays(5);
        current.RowVersion = new byte[] { 7, 8, 9 };
        await setup.Db.SaveChangesAsync();
        var request = new RenewSubscriptionRequestDto
        {
            ClientRequestId = Guid.NewGuid(),
            RowVersion = Convert.ToBase64String(current.RowVersion)
        };
        var renewed = await setup.Service.RenewAsync(current.PublicId, request);
        renewed.Success.Should().BeTrue();
        renewed.Data!.StartsAt.Should().Be(current.EndsAt);
        renewed.Data.State.Should().Be(SubscriptionState.PendingPayment);
        var again = await setup.Service.RenewAsync(current.PublicId, request);
        again.Success.Should().BeTrue();
        again.Data!.Id.Should().Be(renewed.Data.Id);
        (await setup.Db.TenantSubscriptions.CountAsync()).Should().Be(2);
        (await setup.Db.SubscriptionInvoices.CountAsync()).Should().Be(2);
        (await setup.Db.SubscriptionInvoiceLines.CountAsync()).Should().Be(2);
        (await setup.Service.GetCurrentAsync()).Data!.Id.Should().Be(current.Id);
        (await setup.Service.GetUpcomingAsync()).Data!.Id.Should().Be(renewed.Data.Id);
        var other = await setup.Service.RenewAsync(current.PublicId, new RenewSubscriptionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = request.RowVersion
        });
        other.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Renewal_rejects_stale_version_and_requests_too_early()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        var plan = await AddPlanAsync(setup.Db, 800m);
        (await setup.Service.StartAsync(Request(plan.Id))).Success.Should().BeTrue();
        var current = await setup.Db.TenantSubscriptions.SingleAsync();
        current.State = SubscriptionState.Active;
        current.EndsAt = DateTime.UtcNow.AddDays(10);
        current.RowVersion = new byte[] { 4, 5, 6 };
        await setup.Db.SaveChangesAsync();
        var bad = await setup.Service.RenewAsync(current.PublicId, new RenewSubscriptionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        });
        bad.StatusCode.Should().Be(409);
        current.EndsAt = DateTime.UtcNow.AddDays(31);
        await setup.Db.SaveChangesAsync();
        var early = await setup.Service.RenewAsync(current.PublicId, new RenewSubscriptionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = Convert.ToBase64String(current.RowVersion)
        });
        early.StatusCode.Should().Be(409);
        (await setup.Db.TenantSubscriptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Auto_renew_opt_in_schedules_exactly_one_unpaid_invoice()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        var plan = await AddPlanAsync(setup.Db, 900m);
        (await setup.Service.StartAsync(Request(plan.Id))).Success.Should().BeTrue();
        var current = await setup.Db.TenantSubscriptions.SingleAsync();
        current.State = SubscriptionState.Active;
        current.AutoRenew = true;
        current.StartsAt = DateTime.UtcNow.AddDays(-20);
        current.EndsAt = DateTime.UtcNow.AddDays(5);
        await setup.Db.SaveChangesAsync();
        // System job uses a trusted tenant scope; request credentials must not be
        // reused to invoke host-level automatic renewal.
        setup.Accessor.HttpContext = null;
        using (setup.Db.BeginSystemTenantScope(setup.Tenant.Id))
        {
            var created = await setup.Service.ScheduleAutomaticRenewalAsync(setup.Tenant.Id, current.Id);
            created.Success.Should().BeTrue();
            created.Data!.State.Should().Be(SubscriptionState.PendingPayment);
            created.Data.StartsAt.Should().Be(current.EndsAt);
            (await setup.Service.ScheduleAutomaticRenewalAsync(setup.Tenant.Id, current.Id))
                .Data!.Id.Should().Be(created.Data.Id);
        }
        (await setup.Db.TenantSubscriptions.CountAsync()).Should().Be(2);
        (await setup.Db.SubscriptionInvoices.CountAsync()).Should().Be(2);
        (await setup.Db.SubscriptionInvoiceLines.CountAsync()).Should().Be(2);
        var renewalInvoice = await setup.Db.SubscriptionInvoices.OrderByDescending(x => x.Id).FirstAsync();
        renewalInvoice.State.Should().Be(InvoiceState.Issued);
        renewalInvoice.PaidAmount.Should().Be(0m);
        renewalInvoice.DueAmount.Should().Be(900m);
    }

    [Fact]
    public async Task Auto_renew_opt_out_does_not_issue_a_new_invoice()
    {
        await using var setup = await SetupAsync();
        setup.ActivateRequest();
        var plan = await AddPlanAsync(setup.Db, 900m);
        (await setup.Service.StartAsync(Request(plan.Id))).Success.Should().BeTrue();
        var current = await setup.Db.TenantSubscriptions.SingleAsync();
        current.State = SubscriptionState.Active;
        current.AutoRenew = false;
        current.EndsAt = DateTime.UtcNow.AddDays(5);
        await setup.Db.SaveChangesAsync();
        setup.Accessor.HttpContext = null;
        using (setup.Db.BeginSystemTenantScope(setup.Tenant.Id))
        {
            var result = await setup.Service.ScheduleAutomaticRenewalAsync(setup.Tenant.Id, current.Id);
            result.StatusCode.Should().Be(409);
        }
        (await setup.Db.TenantSubscriptions.CountAsync()).Should().Be(1);
        (await setup.Db.SubscriptionInvoices.CountAsync()).Should().Be(1);
    }

    private static StartSubscriptionRequestDto Request(long planId) => new()
    {
        ClientRequestId = Guid.NewGuid(), SubscriptionPlanId = planId,
        BillingCycleCode = "Monthly"
    };

    private static async Task<SubscriptionPlan> AddPlanAsync(EduOSDbContext db, decimal monthly,
        bool isPublic = true, int trialDays = 0)
    {
        var plan = new SubscriptionPlan
        {
            Code = "PLAN-" + Guid.NewGuid().ToString("N")[..12], Name = "School Plan",
            MonthlyPrice = monthly, YearlyPrice = monthly * 12m, CurrencyCode = "BDT",
            TrialDays = trialDays, IsActive = true, IsPublic = isPublic,
            MaxStudents = 300, MaxEmployees = 40, MaxCampuses = 2
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private static async Task<Setup> SetupAsync()
    {
        const long tenantId = 301;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenantId;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "91"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenantId.ToString())
        }, "TestAuthentication"));
        var accessor = new HttpContextAccessor { HttpContext = http };
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("subscription-" + Guid.NewGuid().ToString("N")).Options, accessor);
        var tenant = new Tenant
        {
            Id = tenantId, Name = "Test School", Code = "SCH-" + Guid.NewGuid().ToString("N")[..12],
            Email = "school@example.test", State = TenantState.Active,
            OnboardingStage = OnboardingStage.PlanSelection, EmailVerifiedAt = DateTime.UtcNow
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var user = new TestUser(tenantId);
        var service = new SubscriptionService(
            new TenantSubscriptionRepository(db),
            new SubscriptionPlanRepository(db),
            new SubscriptionInvoiceRepository(db),
            new GenericRepository<SubscriptionInvoiceLine>(db),
            new GenericRepository<Tenant>(db),
            db, user, TimeProvider.System,
            NullLogger<SubscriptionService>.Instance);
        return new Setup(db, tenant, service, accessor, http);
    }

    private sealed record Setup(EduOSDbContext Db, Tenant Tenant, SubscriptionService Service,
        IHttpContextAccessor Accessor, DefaultHttpContext RequestContext) : IAsyncDisposable
    {
        // AsyncLocal changes inside SetupAsync do not flow back into the awaiting
        // test method. Establish the intended request scope explicitly there.
        public void ActivateRequest() => Accessor.HttpContext = RequestContext;
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 91;
        public string? FullName => "Tenant Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => new[] { "TenantAdmin" };
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
