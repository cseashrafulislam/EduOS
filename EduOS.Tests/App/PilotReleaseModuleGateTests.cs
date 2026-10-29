using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class PilotReleaseModuleGateTests
{
    [Theory]
    [InlineData("LMS")]
    [InlineData("library")]
    [InlineData("HOSTEL")]
    [InlineData("transport")]
    [InlineData("Inventory")]
    public async Task Pilot_mode_denies_optional_modules_even_when_tenant_entitled(string module)
    {
        var service = new Mock<ITenantModuleService>(MockBehavior.Strict);
        var context = await AuthorizeAsync(service, module, true);
        Assert.False(context.HasSucceeded);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("STUDENT")]
    [InlineData("ACADEMIC")]
    [InlineData("ADMISSION")]
    [InlineData("ATTENDANCE")]
    [InlineData("EXAM")]
    [InlineData("FINANCE")]
    public async Task Pilot_mode_keeps_core_module_entitlement_checks(string module)
    {
        var service = new Mock<ITenantModuleService>();
        service.Setup(x => x.IsCurrentTenantModuleAvailableAsync(module)).ReturnsAsync(true);
        var context = await AuthorizeAsync(service, module, true);
        Assert.True(context.HasSucceeded);
        service.Verify(x => x.IsCurrentTenantModuleAvailableAsync(module), Times.Once);
    }

    [Fact]
    public async Task Pilot_mode_does_not_grant_unentitled_core_module()
    {
        var service = new Mock<ITenantModuleService>();
        service.Setup(x => x.IsCurrentTenantModuleAvailableAsync("FINANCE")).ReturnsAsync(false);
        Assert.False((await AuthorizeAsync(service, "FINANCE", true)).HasSucceeded);
    }

    [Theory]
    [InlineData("LMS")]
    [InlineData("LIBRARY")]
    [InlineData("HOSTEL")]
    [InlineData("TRANSPORT")]
    [InlineData("INVENTORY")]
    public async Task Normal_mode_preserves_existing_optional_customer_access(string module)
    {
        var service = new Mock<ITenantModuleService>();
        service.Setup(x => x.IsCurrentTenantModuleAvailableAsync(module)).ReturnsAsync(true);
        Assert.True((await AuthorizeAsync(service, module, false)).HasSucceeded);
    }

    [Theory]
    [InlineData(typeof(EduOS.App.Controllers.LearningController), "LMS")]
    [InlineData(typeof(EduOS.App.Controllers.LibraryController), "LIBRARY")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LmsWorkflowController), "LMS")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LmsAssessmentController), "LMS")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LmsCourseContentController), "LMS")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LmsLiveClassesController), "LMS")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LmsAssignmentAttachmentsController), "LMS")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LmsSubmissionFilesController), "LMS")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LibraryController), "LIBRARY")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LibraryBranchesController), "LIBRARY")]
    [InlineData(typeof(EduOS.App.Controllers.Api.LibraryReservationsController), "LIBRARY")]
    [InlineData(typeof(EduOS.App.Controllers.HostelOperationsController), "HOSTEL")]
    [InlineData(typeof(EduOS.App.Controllers.TransportOperationsController), "TRANSPORT")]
    [InlineData(typeof(EduOS.App.Controllers.AssetLifecycleController), "INVENTORY")]
    [InlineData(typeof(EduOS.App.Controllers.InventoryCatalogController), "INVENTORY")]
    [InlineData(typeof(EduOS.App.Controllers.InventoryMovementsController), "INVENTORY")]
    [InlineData(typeof(EduOS.App.Controllers.Api.HostelController), "HOSTEL")]
    [InlineData(typeof(EduOS.App.Controllers.Api.HostelAllocationReadinessController), "HOSTEL")]
    [InlineData(typeof(EduOS.App.Controllers.Api.HostelOccupancyForecastController), "HOSTEL")]
    [InlineData(typeof(EduOS.App.Controllers.Api.HostelPeakOccupancyForecastController), "HOSTEL")]
    [InlineData(typeof(EduOS.App.Controllers.Api.TransportBoardingManifestController), "TRANSPORT")]
    [InlineData(typeof(EduOS.App.Controllers.Api.TransportDriverCoverageForecastController), "TRANSPORT")]
    [InlineData(typeof(EduOS.App.Controllers.Api.TransportController), "TRANSPORT")]
    [InlineData(typeof(EduOS.App.Controllers.Api.TransportDriverReadinessController), "TRANSPORT")]
    [InlineData(typeof(EduOS.App.Controllers.Api.TransportDriverCoverageWindowController), "TRANSPORT")]
    [InlineData(typeof(EduOS.App.Controllers.Api.TransportStopDemandController), "TRANSPORT")]
    [InlineData(typeof(EduOS.App.Controllers.Api.AssetLifecycleController), "INVENTORY")]
    public void Pilot_optional_controller_routes_are_protected_by_module_policy(Type controller, string code)
    {
        var attribute = Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>());
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + code, attribute.Policy);
    }

    private static async Task<AuthorizationHandlerContext> AuthorizeAsync(
        Mock<ITenantModuleService> service, string module, bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PilotRelease:Enabled"] = enabled.ToString()
            }).Build();
        var requirement = new ModuleAccessRequirement(module);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "123"),
            new Claim("TenantId", "42")
        }, "Test"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);
        await new ModuleAccessHandler(service.Object, configuration).HandleAsync(context);
        return context;
    }
}
