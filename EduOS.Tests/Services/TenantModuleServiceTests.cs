using EduOS.Core.Entities.SaaS;
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

public sealed class TenantModuleServiceTests
{
    [Fact]
    public async Task Preset_and_paid_subscription_both_control_module_availability()
    {
        await using var setup = await CreateAsync("BASIC", SubscriptionState.Active);
        var service = Service(setup.Context, setup.User);
        var applied = await service.ApplyInstitutionPresetAsync(setup.Tenant.Id,
            setup.Tenant.InstitutionTypeDefinitionId!.Value);
        applied.Success.Should().BeTrue();
        var state = await service.GetCurrentTenantModulesAsync();
        state.Success.Should().BeTrue();
        var student = state.Data!.Single(x => x.ModuleCode == "STUDENT");
        student.IsEnabled.Should().BeTrue();
        student.IsEntitledByPlan.Should().BeTrue();
        var library = state.Data.Single(x => x.ModuleCode == "LIBRARY");
        library.IsEnabled.Should().BeTrue();
        library.IsEntitledByPlan.Should().BeFalse();
        (await service.IsCurrentTenantModuleAvailableAsync("LIBRARY")).Should().BeFalse();
    }

    [Fact]
    public async Task Pending_subscription_does_not_grant_paid_modules()
    {
        await using var setup = await CreateAsync("PRO", SubscriptionState.PendingPayment);
        var service = Service(setup.Context, setup.User);
        (await service.ApplyInstitutionPresetAsync(setup.Tenant.Id,
            setup.Tenant.InstitutionTypeDefinitionId!.Value)).Success.Should().BeTrue();
        var state = await service.GetCurrentTenantModulesAsync();
        state.Success.Should().BeTrue();
        state.Data!.Single(x => x.ModuleCode == "LIBRARY").IsEntitledByPlan.Should().BeFalse();
        state.Data.Single(x => x.ModuleCode == "CORE_ADMIN").IsEntitledByPlan.Should().BeTrue();
    }

    [Fact]
    public async Task Required_student_module_cannot_be_disabled()
    {
        await using var setup = await CreateAsync("BASIC", SubscriptionState.Active);
        var service = Service(setup.Context, setup.User);
        (await service.ApplyInstitutionPresetAsync(setup.Tenant.Id,
            setup.Tenant.InstitutionTypeDefinitionId!.Value)).Success.Should().BeTrue();
        var denied = await service.UpdateCurrentTenantModuleAsync("student", new() { IsEnabled = false });
        denied.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Excluded_module_cannot_be_enabled_by_tenant_administrator()
    {
        await using var setup = await CreateAsync("BASIC", SubscriptionState.Active);
        var service = Service(setup.Context, setup.User);
        var denied = await service.UpdateCurrentTenantModuleAsync("LIBRARY", new() { IsEnabled = true });
        denied.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Existing_module_change_requires_matching_rowversion()
    {
        await using var setup = await CreateAsync("PRO", SubscriptionState.Active);
        var libraryId = await setup.Context.ProductModules.Where(x => x.Code == "LIBRARY")
            .Select(x => x.Id).SingleAsync();
        setup.Context.TenantModules.Add(new TenantModule
        {
            TenantId = setup.Tenant.Id, ProductModuleId = libraryId,
            IsEnabled = true, EnabledAt = DateTime.UtcNow
        });
        await setup.Context.SaveChangesAsync();
        var service = Service(setup.Context, setup.User);
        var missing = await service.UpdateCurrentTenantModuleAsync("LIBRARY",
            new() { IsEnabled = false });
        missing.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Required_modules_pass_onboarding_validation_for_basic_plan()
    {
        await using var setup = await CreateAsync("BASIC", SubscriptionState.Active);
        var service = Service(setup.Context, setup.User);
        (await service.ApplyInstitutionPresetAsync(setup.Tenant.Id,
            setup.Tenant.InstitutionTypeDefinitionId!.Value)).Success.Should().BeTrue();
        var check = await service.ValidateCurrentTenantSelectionAsync();
        check.Success.Should().BeTrue();
    }

    private static TenantModuleService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<Tenant>(db),
        new GenericRepository<ProductModule>(db),
        new GenericRepository<InstitutionTypeModule>(db),
        new GenericRepository<TenantModule>(db),
        new GenericRepository<ProductModuleFeature>(db),
        new GenericRepository<PlanFeature>(db),
        new GenericRepository<TenantSubscription>(db),
        db, user, NullLogger<TenantModuleService>.Instance);

    private static async Task<Setup> CreateAsync(string planCode, SubscriptionState state)
    {
        var http = new DefaultHttpContext();
        var accessor = new TestHttpContextAccessor { HttpContext = http };
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("tenant-module-" + Guid.NewGuid().ToString("N")).Options, accessor);
        await SubscriptionSeeder.SeedAsync(db);
        await PlatformCatalogSeeder.SeedAsync(db);
        var type = await db.InstitutionTypeDefinitions.SingleAsync(x => x.Code == "PRIMARY_SCHOOL");
        var tenant = new Tenant
        {
            Name = "School", Code = "TEST-" + Guid.NewGuid().ToString("N")[..12],
            Email = "school@example.test", InstitutionTypeDefinitionId = type.Id,
            State = TenantState.Active, EmailVerifiedAt = DateTime.UtcNow
        };
        db.Tenants.Add(tenant); await db.SaveChangesAsync();
        http.Items["TenantId"] = tenant.Id;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.Id.ToString())
        }, "TestAuthentication"));
        var plan = await db.SubscriptionPlans.SingleAsync(x => x.Code == planCode);
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id, SubscriptionPlanId = plan.Id,
            StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(30),
            State = state, BillingCycleCode = "Monthly", CurrencyCode = "BDT",
            PriceSnapshot = plan.MonthlyPrice
        });
        await db.SaveChangesAsync();
        return new Setup(db, tenant, new TestUser(tenant.Id));
    }

    private sealed record Setup(EduOSDbContext Context, Tenant Tenant, TestUser User) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TestUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 99;
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
