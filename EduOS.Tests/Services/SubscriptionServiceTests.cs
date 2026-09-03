using AutoMapper;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Repositories.SaaS;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class SubscriptionServiceTests
{
    [Fact]
    public async Task Paid_subscription_includes_setup_fee_and_advances_to_payment()
    {
        await using var setup = await CreateSetupAsync();
        var plan = await setup.AddPlanAsync(monthlyPrice: 1_000m, setupFee: 250m);

        var result = await setup.Service.CreateAsync(new CreateSubscriptionRequestDto
        {
            SubscriptionPlanId = plan.Id,
            BillingCycle = BillingCycle.Monthly
        });

        result.Success.Should().BeTrue();
        result.Data!.Amount.Should().Be(1_250m);
        var invoice = await setup.Context.SubscriptionInvoices.SingleAsync();
        invoice.Subtotal.Should().Be(1_250m);
        invoice.TotalAmount.Should().Be(1_250m);
        invoice.DueAmount.Should().Be(1_250m);
        setup.Tenant.OnboardingStep.Should().Be(OnboardingStep.Payment);
    }

    [Fact]
    public async Task Trial_subscription_creates_no_invoice_and_advances_to_campus_setup()
    {
        await using var setup = await CreateSetupAsync();
        var plan = await setup.AddPlanAsync(isTrial: true, trialDays: 14, setupFee: 500m);

        var result = await setup.Service.CreateAsync(new CreateSubscriptionRequestDto
        {
            SubscriptionPlanId = plan.Id,
            BillingCycle = BillingCycle.Monthly
        });

        result.Success.Should().BeTrue();
        result.Data!.IsTrialActivated.Should().BeTrue();
        result.Data.Amount.Should().Be(0);
        (await setup.Context.SubscriptionInvoices.CountAsync()).Should().Be(0);
        setup.Tenant.OnboardingStep.Should().Be(OnboardingStep.CampusSetup);
    }

    [Fact]
    public async Task Hidden_plan_cannot_be_selected_by_id()
    {
        await using var setup = await CreateSetupAsync();
        var plan = await setup.AddPlanAsync(isPublic: false, monthlyPrice: 500m);

        var result = await setup.Service.CreateAsync(new CreateSubscriptionRequestDto
        {
            SubscriptionPlanId = plan.Id
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        (await setup.Context.TenantSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Existing_pending_subscription_blocks_duplicate_invoice_creation()
    {
        await using var setup = await CreateSetupAsync();
        var plan = await setup.AddPlanAsync(monthlyPrice: 500m);
        setup.Context.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = setup.Tenant.Id,
            SubscriptionPlanId = plan.Id,
            BillingCycle = BillingCycle.Monthly,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddMonths(1),
            Currency = "BDT"
        });
        await setup.Context.SaveChangesAsync();

        var result = await setup.Service.CreateAsync(new CreateSubscriptionRequestDto
        {
            SubscriptionPlanId = plan.Id
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        (await setup.Context.TenantSubscriptions.CountAsync()).Should().Be(1);
        (await setup.Context.SubscriptionInvoices.CountAsync()).Should().Be(0);
    }

    private static async Task<TestSetup> CreateSetupAsync()
    {
        const long tenantId = 301;
        var httpContext = new DefaultHttpContext();
        httpContext.Items["TenantId"] = tenantId;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "91"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication"));
        var accessor = new TestHttpContextAccessor { HttpContext = httpContext };
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"subscription-service-{Guid.NewGuid():N}")
            .Options;
        var context = new EduOSDbContext(options, accessor);
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test School",
            Code = $"SCH-{Guid.NewGuid():N}"[..20],
            Email = "school@example.test",
            OwnerName = "Owner",
            InstitutionType = "School",
            OnboardingStep = OnboardingStep.PlanSelection,
            IsEmailVerified = true
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var unitOfWork = new NoTransactionUnitOfWork(context);
        var service = new SubscriptionService(
            new TenantSubscriptionRepository(context),
            new SubscriptionPlanRepository(context),
            new SubscriptionInvoiceRepository(context),
            new GenericRepository<Tenant>(context),
            unitOfWork,
            new TestCurrentUser(tenantId),
            Mock.Of<IMapper>(),
            Options.Create(new ManualPaymentSettings()),
            NullLogger<SubscriptionService>.Instance);

        return new TestSetup(context, tenant, service);
    }

    private sealed record TestSetup(
        EduOSDbContext Context,
        Tenant Tenant,
        SubscriptionService Service) : IAsyncDisposable
    {
        public async Task<SubscriptionPlan> AddPlanAsync(
            bool isPublic = true,
            bool isTrial = false,
            int? trialDays = null,
            decimal monthlyPrice = 0,
            decimal setupFee = 0)
        {
            var plan = new SubscriptionPlan
            {
                Name = isTrial ? "Trial" : "Basic",
                Code = $"PLAN-{Guid.NewGuid():N}"[..20],
                IsActive = true,
                IsPubliclyVisible = isPublic,
                IsFreeTrial = isTrial,
                TrialDays = trialDays,
                MonthlyPrice = monthlyPrice,
                QuarterlyPrice = monthlyPrice * 3,
                HalfYearlyPrice = monthlyPrice * 6,
                YearlyPrice = monthlyPrice * 12,
                SetupFee = setupFee,
                Currency = "BDT",
                MaxStudents = 500,
                MaxTeachers = 50,
                MaxCampuses = 2,
                MaxStorageMb = 1_024
            };
            Context.SubscriptionPlans.Add(plan);
            await Context.SaveChangesAsync();
            return plan;
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class NoTransactionUnitOfWork(EduOSDbContext context) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            context.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync() => Task.CompletedTask;
        public Task CommitTransactionAsync() => Task.CompletedTask;
        public Task RollbackTransactionAsync() => Task.CompletedTask;
        public IExecutionStrategy CreateExecutionStrategy() => context.Database.CreateExecutionStrategy();
        public void Dispose() { }
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 91;
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
