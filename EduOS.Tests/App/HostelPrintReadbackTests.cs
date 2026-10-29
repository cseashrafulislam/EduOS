using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class HostelPrintReadbackTests
{
    [Fact]
    public async Task Resident_print_uses_authorized_paged_service_result()
    {
        var service = new Mock<IHostelService>();
        var date = new DateOnly(2026, 10, 29);
        var page = new PagedResult<HostelResidentRosterRowDto>
        {
            Page = 2, PageSize = 25, TotalCount = 26,
            Items = new List<HostelResidentRosterRowDto>()
        };
        service.Setup(x => x.GetResidentRosterAsync(date, 7, 2, 25, "student",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<HostelResidentRosterRowDto>>.SuccessResponse(page));
        var controller = new HostelOperationsController(service.Object);
        var result = await controller.PrintResidentRoster(date, 7, 2, "student");
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ResidentRosterPrint", view.ViewName);
        Assert.Same(page, view.Model);
        Assert.Equal("2026-10-29", controller.ViewData["OperatingDate"]);
        service.Verify(x => x.GetResidentRosterAsync(date, 7, 2, 25, "student",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task History_print_propagates_service_denial_without_rendering_private_data()
    {
        var service = new Mock<IHostelService>();
        service.Setup(x => x.GetAllocationHistoryAsync(8, 1, 25, null,
            HostelAllocationState.Closed, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>
                .ErrorResponse("Hostel permission is required.", 403));
        var controller = new HostelOperationsController(service.Object);
        var result = await controller.PrintAllocationHistory(8, 1, null,
            HostelAllocationState.Closed);
        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, denied.StatusCode);
    }

    [Theory]
    [InlineData(nameof(HostelOperationsController.PrintResidentRoster))]
    [InlineData(nameof(HostelOperationsController.PrintAllocationHistory))]
    public void Print_routes_require_manager_role_rate_limit_and_no_store(string action)
    {
        var type = typeof(HostelOperationsController);
        Assert.Single(type.GetCustomAttributes(typeof(RequireModuleAttribute), true));
        var method = type.GetMethod(action)!;
        var role = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin,Principal,HostelWarden", role.Roles);
        Assert.Single(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true));
        var cache = Assert.Single(method.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore);
    }
}
