using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class OnboardingServiceTests
{
    [Fact]
    public async Task Non_current_step_cannot_be_used_to_jump_forward()
    {
        await using var setup = await CreateSetupAsync(OnboardingStep.AcademicSetup);

        var result = await setup.Service.CompleteStepAsync(new CompleteStepDto
        {
            Step = OnboardingStep.ModuleSetup
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingStep.Should().Be(OnboardingStep.AcademicSetup);
    }

    [Fact]
    public async Task Academic_step_requires_a_year_before_module_selection()
    {
        await using var setup = await CreateSetupAsync(OnboardingStep.AcademicSetup);

        var missing = await setup.Service.CompleteStepAsync(new CompleteStepDto
        {
            Step = OnboardingStep.AcademicSetup
        });
        missing.Success.Should().BeFalse();
        missing.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingStep.Should().Be(OnboardingStep.AcademicSetup);
    }

    [Fact]
    public async Task Academic_step_with_a_year_advances_to_module_selection()
    {
        await using var setup = await CreateSetupAsync(
            OnboardingStep.AcademicSetup,
            hasAcademicYear: true);
        var completed = await setup.Service.CompleteStepAsync(new CompleteStepDto
        {
            Step = OnboardingStep.AcademicSetup
        });

        completed.Success.Should().BeTrue(completed.Message);
        setup.Tenant.OnboardingStep.Should().Be(OnboardingStep.ModuleSetup);
    }

    [Fact]
    public async Task Module_step_uses_server_entitlement_validation()
    {
        var validation = ApiResponse<bool>.ErrorResponse(
            "Required module unavailable", 409);
        await using var setup = await CreateSetupAsync(
            OnboardingStep.ModuleSetup,
            validation);

        var result = await setup.Service.CompleteStepAsync(new CompleteStepDto
        {
            Step = OnboardingStep.ModuleSetup
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingStep.Should().Be(OnboardingStep.ModuleSetup);
    }

    [Fact]
    public async Task Valid_module_step_advances_to_branding_without_changing_enum_values()
    {
        await using var setup = await CreateSetupAsync(OnboardingStep.ModuleSetup);

        var result = await setup.Service.CompleteStepAsync(new CompleteStepDto
        {
            Step = OnboardingStep.ModuleSetup
        });

        result.Success.Should().BeTrue();
        setup.Tenant.OnboardingStep.Should().Be(OnboardingStep.BrandingSetup);
        ((int)OnboardingStep.BrandingSetup).Should().Be(6);
        ((int)OnboardingStep.ModuleSetup).Should().Be(9);
    }

    [Fact]
    public async Task Final_completion_cannot_bypass_remaining_steps()
    {
        await using var setup = await CreateSetupAsync(OnboardingStep.ModuleSetup);

        var result = await setup.Service.CompleteOnboardingAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        setup.Tenant.IsOnboardingComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Status_uses_flow_order_instead_of_persisted_enum_number()
    {
        await using var setup = await CreateSetupAsync(OnboardingStep.ModuleSetup);

        var result = await setup.Service.GetStatusAsync();

        result.Success.Should().BeTrue();
        result.Data!.TotalSteps.Should().Be(10);
        var academic = result.Data.Steps.Single(x => x.Step == OnboardingStep.AcademicSetup);
        var modules = result.Data.Steps.Single(x => x.Step == OnboardingStep.ModuleSetup);
        var branding = result.Data.Steps.Single(x => x.Step == OnboardingStep.BrandingSetup);
        academic.Order.Should().BeLessThan(modules.Order);
        modules.Order.Should().BeLessThan(branding.Order);
        modules.IsCurrent.Should().BeTrue();
        branding.IsLocked.Should().BeTrue();
    }

    private static async Task<TestSetup> CreateSetupAsync(
        OnboardingStep step,
        ApiResponse<bool>? moduleValidation = null,
        bool hasAcademicYear = false)
    {
        const long tenantId = 410;
        var httpContext = new DefaultHttpContext();
        httpContext.Items["TenantId"] = tenantId;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "71"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication"));
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"onboarding-{Guid.NewGuid():N}")
            .Options;
        var context = new EduOSDbContext(options, accessor);
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test Institution",
            Code = "ONBOARDING-TEST",
            InstitutionType = "PRIMARY_SCHOOL",
            Email = "admin@example.test",
            OwnerName = "Tenant Owner",
            IsEmailVerified = true,
            OnboardingStep = step,
            Status = TenantStatus.Onboarding
        };
        context.Tenants.Add(tenant);
        if (hasAcademicYear)
        {
            context.AcademicYears.Add(new AcademicYear
            {
                TenantId = tenantId,
                Name = "2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsCurrent = true
            });
        }
        await context.SaveChangesAsync();

        var moduleService = new Mock<ITenantModuleService>();
        moduleService
            .Setup(x => x.ValidateCurrentTenantSelectionAsync())
            .ReturnsAsync(moduleValidation ?? ApiResponse<bool>.SuccessResponse(true));
        var subscriptionRepo = new Mock<ITenantSubscriptionRepository>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new OnboardingService(
            new GenericRepository<Tenant>(context),
            new GenericRepository<Campus>(context),
            new GenericRepository<AcademicYear>(context),
            subscriptionRepo.Object,
            moduleService.Object,
            context,
            new TestCurrentUser(tenantId),
            cache,
            NullLogger<OnboardingService>.Instance);

        return new TestSetup(context, tenant, service, cache);
    }

    private sealed record TestSetup(
        EduOSDbContext Context,
        Tenant Tenant,
        OnboardingService Service,
        MemoryCache Cache) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Cache.Dispose();
            await Context.DisposeAsync();
        }
    }

    private sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 71;
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
