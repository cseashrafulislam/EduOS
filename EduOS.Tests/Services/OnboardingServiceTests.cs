using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class OnboardingServiceTests
{
    [Fact]
    public async Task Cannot_skip_required_stage()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.BrandingSetup);
        var result = await setup.Service.CompleteStageAsync(new CompleteOnboardingStageRequestDto
        {
            Stage = OnboardingStage.BrandingSetup, Skipped = true
        });
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingStage.Should().Be(OnboardingStage.BrandingSetup);
    }

    [Fact]
    public async Task Cannot_submit_another_stage_or_jump_forward()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.AcademicSetup);
        var mismatch = await setup.Service.CompleteStageAsync(new CompleteOnboardingStageRequestDto
        {
            Stage = OnboardingStage.ModuleSetup
        });
        var jump = await setup.Service.AdvanceToStageAsync(OnboardingStage.BrandingSetup);
        mismatch.StatusCode.Should().Be(409);
        jump.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingStage.Should().Be(OnboardingStage.AcademicSetup);
    }

    [Fact]
    public async Task Academic_stage_requires_active_tenant_year()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.AcademicSetup);
        var result = await setup.Service.CompleteStageAsync(new CompleteOnboardingStageRequestDto
        {
            Stage = OnboardingStage.AcademicSetup
        });
        result.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingStage.Should().Be(OnboardingStage.AcademicSetup);
    }

    [Fact]
    public async Task Academic_stage_with_year_advances_to_modules()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.AcademicSetup, hasYear: true);
        var result = await setup.Service.CompleteStageAsync(new CompleteOnboardingStageRequestDto
        {
            Stage = OnboardingStage.AcademicSetup
        });
        result.Success.Should().BeTrue(result.Message);
        setup.Tenant.OnboardingStage.Should().Be(OnboardingStage.ModuleSetup);
    }

    [Fact]
    public async Task Module_stage_enforces_entitlement_validation()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.ModuleSetup,
            moduleValidation: ApiResponse<bool>.ErrorResponse("No available modules.", 409));
        var result = await setup.Service.CompleteStageAsync(new CompleteOnboardingStageRequestDto
        {
            Stage = OnboardingStage.ModuleSetup
        });
        result.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingStage.Should().Be(OnboardingStage.ModuleSetup);
    }

    [Fact]
    public async Task Optional_general_settings_can_be_skipped()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.GeneralSettings);
        var result = await setup.Service.CompleteStageAsync(new CompleteOnboardingStageRequestDto
        {
            Stage = OnboardingStage.GeneralSettings, Skipped = true
        });
        result.Success.Should().BeTrue(result.Message);
        setup.Tenant.OnboardingStage.Should().Be(OnboardingStage.GatewaySetup);
    }

    [Fact]
    public async Task Finalization_cannot_skip_remaining_stages()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.ModuleSetup);
        var result = await setup.Service.CompleteOnboardingAsync();
        result.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingCompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Gateway_finalization_rechecks_required_prerequisites()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.GatewaySetup);
        var result = await setup.Service.CompleteOnboardingAsync();
        result.StatusCode.Should().Be(409);
        setup.Tenant.OnboardingCompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Gateway_finalization_succeeds_when_required_steps_are_valid()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.GatewaySetup, hasYear: true, hasCampus: true);
        var result = await setup.Service.CompleteStageAsync(new CompleteOnboardingStageRequestDto
        {
            Stage = OnboardingStage.GatewaySetup, Skipped = true
        });
        result.Success.Should().BeTrue(result.Message);
        setup.Tenant.OnboardingStage.Should().Be(OnboardingStage.Completed);
        setup.Tenant.OnboardingCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Status_uses_canonical_stage_order_and_progress()
    {
        await using var setup = await CreateSetupAsync(OnboardingStage.ModuleSetup);
        var result = await setup.Service.GetStatusAsync();
        result.Success.Should().BeTrue();
        result.Data!.CurrentStage.Should().Be(OnboardingStage.ModuleSetup);
        result.Data.TotalStages.Should().Be(10);
        result.Data.CompletedStages.Should().Be(6);
        result.Data.Stages.Single(x => x.Stage == OnboardingStage.ModuleSetup).IsCurrent.Should().BeTrue();
        result.Data.Stages.Single(x => x.Stage == OnboardingStage.BrandingSetup).IsLocked.Should().BeTrue();
    }

    private static async Task<TestSetup> CreateSetupAsync(OnboardingStage stage,
        bool hasYear = false, bool hasCampus = false, ApiResponse<bool>? moduleValidation = null)
    {
        const long tenantId = 410;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenantId;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "71"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenantId.ToString())
        }, "TestAuthentication"));
        var accessor = new TestHttpContextAccessor { HttpContext = http };
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("onboarding-canonical-" + Guid.NewGuid().ToString("N")).Options;
        var context = new EduOSDbContext(options, accessor);
        var tenant = new Tenant
        {
            Id = tenantId, Name = "Test Institution", Code = "ONBOARDING-TEST",
            Email = "admin@example.test", InstitutionTypeDefinitionId = 1,
            EmailVerifiedAt = DateTime.UtcNow.AddDays(-1), Subdomain = "school",
            OnboardingStage = stage, State = TenantState.Active
        };
        context.Tenants.Add(tenant);
        if (hasCampus)
            context.Campuses.Add(new Campus
            {
                TenantId = tenantId, Name = "Main", Code = "MAIN", IsActive = true
            });
        if (hasYear)
            context.AcademicYears.Add(new AcademicYear
            {
                TenantId = tenantId, Name = "2026", Code = "2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
                IsCurrent = true, IsActive = true
            });
        await context.SaveChangesAsync();

        var modules = new Mock<ITenantModuleService>();
        modules.Setup(x => x.ValidateCurrentTenantSelectionAsync())
            .ReturnsAsync(moduleValidation ?? ApiResponse<bool>.SuccessResponse(true));
        var subscriptions = new Mock<ITenantSubscriptionRepository>();
        subscriptions.Setup(x => x.GetActiveByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantSubscription
            {
                TenantId = tenantId, SubscriptionPlanId = 1, State = SubscriptionState.Trial,
                IsTrial = true, StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(30)
            });

        var service = new OnboardingService(new GenericRepository<Tenant>(context),
            new GenericRepository<Campus>(context), new GenericRepository<AcademicYear>(context),
            subscriptions.Object, modules.Object, context, new TestCurrentUser(tenantId),
            TimeProvider.System, NullLogger<OnboardingService>.Instance);
        return new TestSetup(context, tenant, service);
    }

    private sealed record TestSetup(EduOSDbContext Context, Tenant Tenant, OnboardingService Service) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
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

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
