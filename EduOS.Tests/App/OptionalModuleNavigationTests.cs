using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class OptionalModuleNavigationTests
{
    [Fact]
    public async Task One_tenant_scoped_read_filters_disabled_and_unentitled_modules()
    {
        var service = ModuleService(
            Module("lms", true, true),
            Module("LIBRARY", false, true),
            Module("HOSTEL", true, false),
            Module("transport", true, true),
            Module("INVENTORY", true, true),
            Module("HR", true, true));
        var available = await OptionalModuleNavigation.GetAvailableAsync(service.Object, false, true);
        Assert.Equal(3, available.Count);
        Assert.True(available.Contains("LMS"));
        Assert.True(available.Contains("TRANSPORT"));
        Assert.True(available.Contains("INVENTORY"));
        Assert.DoesNotContain("LIBRARY", available);
        Assert.DoesNotContain("HOSTEL", available);
        Assert.DoesNotContain("HR", available);
        service.Verify(x => x.GetCurrentTenantModulesAsync(), Times.Once);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Pilot_or_missing_authenticated_membership_does_not_query_tenant_modules(
        bool pilot, bool member)
    {
        var service = new Mock<ITenantModuleService>(MockBehavior.Strict);
        Assert.Empty(await OptionalModuleNavigation.GetAvailableAsync(service.Object, pilot, member));
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Failed_module_read_fails_closed()
    {
        var service = new Mock<ITenantModuleService>();
        service.Setup(x => x.GetCurrentTenantModulesAsync())
            .ReturnsAsync(ApiResponse<List<TenantModuleDto>>.ErrorResponse("Unavailable", 500));
        Assert.Empty(await OptionalModuleNavigation.GetAvailableAsync(service.Object, false, true));
    }

    [Fact]
    public async Task Consecutive_institute_views_do_not_reuse_previous_entitlements()
    {
        var first = ModuleService(Module("LMS", true, true));
        var second = ModuleService(Module("HOSTEL", true, true));
        var firstModules = await OptionalModuleNavigation.GetAvailableAsync(first.Object, false, true);
        var secondModules = await OptionalModuleNavigation.GetAvailableAsync(second.Object, false, true);
        Assert.Contains("LMS", firstModules);
        Assert.DoesNotContain("LMS", secondModules);
        Assert.Contains("HOSTEL", secondModules);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public async Task Overview_only_renders_authorized_module_sections(
        bool transport, bool hostel, bool allowed)
    {
        var service = ModuleService(
            Module("TRANSPORT", transport, transport),
            Module("HOSTEL", hostel, hostel));
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PilotRelease:Enabled"] = "false" }).Build();
        var controller = new OperationsInsightsController(service.Object, config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "123"),
                        new Claim(ClaimTypes.Role, "Principal")
                    }, "Test"))
                }
            }
        };
        var result = await controller.Index();
        if (!allowed)
        {
            Assert.IsType<ForbidResult>(result);
            return;
        }
        Assert.IsType<ViewResult>(result);
        Assert.Equal(transport, Assert.IsType<bool>(controller.ViewData["ShowTransport"]));
        Assert.Equal(hostel, Assert.IsType<bool>(controller.ViewData["ShowHostel"]));
    }

    [Fact]
    public async Task Overview_pilot_rejects_direct_url_without_entitlement_read()
    {
        var service = new Mock<ITenantModuleService>(MockBehavior.Strict);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PilotRelease:Enabled"] = "true" }).Build();
        var controller = new OperationsInsightsController(service.Object, config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Role, "TenantAdmin")
                    }, "Test"))
                }
            }
        };
        Assert.IsType<ForbidResult>(await controller.Index());
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public void Shared_layout_guards_all_optional_navigation_and_preserves_hr()
    {
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "_Layout.cshtml"));
        foreach (var guard in new[] { "showLms", "showLibrary", "showHostel", "showTransport", "showInventory" })
            Assert.Contains("availableOptionalModules.Contains", view);
        Assert.Contains("@if (showLms &&", view);
        Assert.Contains("@if (showInventory &&", view);
        Assert.Contains("@if (showLibrary)", view);
        Assert.Contains("@if (showHostel)", view);
        Assert.Contains("@if (showTransport)", view);
        Assert.Contains("@if (showHostel || showTransport)", view);
        Assert.Contains("@ActiveFor(\"/PayrollOperations\")", view);
    }

    private static TenantModuleDto Module(string code, bool enabled, bool entitled) =>
        new() { ModuleCode = code, IsEnabled = enabled, IsEntitledByPlan = entitled };

    private static Mock<ITenantModuleService> ModuleService(params TenantModuleDto[] modules)
    {
        var service = new Mock<ITenantModuleService>();
        service.Setup(x => x.GetCurrentTenantModulesAsync())
            .ReturnsAsync(ApiResponse<List<TenantModuleDto>>.SuccessResponse(modules.ToList()));
        return service;
    }
}
