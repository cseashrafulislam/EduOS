using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
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

public class TenantModuleServiceTests
{
    [Fact]
    public async Task Preset_selection_and_paid_plan_are_both_required_for_module_availability()
    {
        var setup = await CreateSetupAsync("BASIC", SubscriptionStatus.Active);
        await using var context = setup.Context;
        var service = CreateService(context, setup.CurrentUser);

        (await service.ApplyInstitutionPresetAsync(
            setup.Tenant.Id,
            setup.Tenant.InstitutionTypeDefinitionId!.Value)).Succeeded.Should().BeTrue();

        var result = await service.GetCurrentTenantModulesAsync();

        result.Success.Should().BeTrue();
        result.Data!.Single(x => x.Code == "STUDENT").IsAvailable.Should().BeTrue();
        var library = result.Data.Single(x => x.Code == "LIBRARY");
        library.IsSelected.Should().BeTrue();
        library.IsIncludedInPlan.Should().BeFalse();
        library.IsAvailable.Should().BeFalse();
        library.AvailabilityReasonCode.Should().Be("NOT_INCLUDED_IN_PLAN");
    }

    [Fact]
    public async Task Pending_payment_never_grants_paid_module_access()
    {
        var setup = await CreateSetupAsync("PRO", SubscriptionStatus.PendingPayment);
        await using var context = setup.Context;
        var service = CreateService(context, setup.CurrentUser);
        await service.ApplyInstitutionPresetAsync(
            setup.Tenant.Id,
            setup.Tenant.InstitutionTypeDefinitionId!.Value);

        var result = await service.GetCurrentTenantModulesAsync();

        result.Data!.Single(x => x.Code == "LIBRARY").IsAvailable.Should().BeFalse();
        result.Data.Single(x => x.Code == "CORE_ADMIN").IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Required_module_cannot_be_disabled_even_by_tenant_admin()
    {
        var setup = await CreateSetupAsync("BASIC", SubscriptionStatus.Active);
        await using var context = setup.Context;
        var service = CreateService(context, setup.CurrentUser);
        await service.ApplyInstitutionPresetAsync(
            setup.Tenant.Id,
            setup.Tenant.InstitutionTypeDefinitionId!.Value);

        var result = await service.UpdateCurrentTenantModuleAsync(
            "student",
            new() { IsEnabled = false, DisabledReason = "Not needed" });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Tenant_cannot_enable_module_excluded_from_active_plan()
    {
        var setup = await CreateSetupAsync("BASIC", SubscriptionStatus.Active);
        await using var context = setup.Context;
        var service = CreateService(context, setup.CurrentUser);

        var result = await service.UpdateCurrentTenantModuleAsync(
            "library",
            new() { IsEnabled = true });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Existing_module_selection_requires_a_concurrency_token()
    {
        var setup = await CreateSetupAsync("PRO", SubscriptionStatus.Active);
        await using var context = setup.Context;
        var service = CreateService(context, setup.CurrentUser);
        var library = await context.ProductModules.SingleAsync(x => x.Code == "LIBRARY");
        context.TenantModules.Add(new TenantModule
        {
            TenantId = setup.Tenant.Id,
            ProductModuleId = library.Id,
            IsEnabled = true,
            ActivationSource = TenantModuleActivationSource.InstitutionPreset,
            EnabledAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        (await context.TenantModules.CountAsync()).Should().Be(1);

        var result = await service.UpdateCurrentTenantModuleAsync(
            "library",
            new() { IsEnabled = false });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(428);
    }

    [Fact]
    public async Task Seeded_required_modules_form_a_valid_onboarding_selection()
    {
        var setup = await CreateSetupAsync("BASIC", SubscriptionStatus.Active);
        await using var context = setup.Context;
        var service = CreateService(context, setup.CurrentUser);
        await service.ApplyInstitutionPresetAsync(
            setup.Tenant.Id,
            setup.Tenant.InstitutionTypeDefinitionId!.Value);

        var result = await service.ValidateCurrentTenantSelectionAsync();

        result.Success.Should().BeTrue();
    }

    private static TenantModuleService CreateService(
        EduOSDbContext context,
        ICurrentUserService currentUser)
    {
        return new TenantModuleService(
            new GenericRepository<Tenant>(context),
            new GenericRepository<ProductModule>(context),
            new GenericRepository<InstitutionTypeModule>(context),
            new GenericRepository<TenantModule>(context),
            new GenericRepository<ProductModuleFeature>(context),
            new GenericRepository<PlanFeature>(context),
            new GenericRepository<TenantSubscription>(context),
            context,
            currentUser,
            NullLogger<TenantModuleService>.Instance);
    }

    private static async Task<TestSetup> CreateSetupAsync(
        string planCode,
        SubscriptionStatus subscriptionStatus)
    {
        var httpContext = new DefaultHttpContext();
        // HttpContextAccessor uses a shared AsyncLocal and is unsafe as a fixture
        // when xUnit runs tenant-scoped test classes in parallel.
        var accessor = new TestHttpContextAccessor { HttpContext = httpContext };
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"tenant-module-{Guid.NewGuid():N}")
            .Options;
        var context = new EduOSDbContext(options, accessor);

        await SubscriptionSeeder.SeedAsync(context);
        await PlatformCatalogSeeder.SeedAsync(context);

        var institutionType = await context.InstitutionTypeDefinitions
            .SingleAsync(x => x.Code == "PRIMARY_SCHOOL");
        var tenant = new Tenant
        {
            Name = "Test School",
            Code = $"TEST-{Guid.NewGuid():N}"[..20],
            Email = "school@example.test",
            OwnerName = "Owner",
            InstitutionType = institutionType.Code,
            InstitutionTypeDefinitionId = institutionType.Id
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        httpContext.Items["TenantId"] = tenant.Id;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.Id.ToString())
        ], "TestAuthentication"));

        var plan = await context.SubscriptionPlans.SingleAsync(x => x.Code == planCode);
        context.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30),
            Status = subscriptionStatus,
            Currency = "BDT"
        });
        await context.SaveChangesAsync();

        return new TestSetup(context, tenant, new TestCurrentUser(tenant.Id));
    }

    private sealed record TestSetup(
        EduOSDbContext Context,
        Tenant Tenant,
        ICurrentUserService CurrentUser);

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

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
