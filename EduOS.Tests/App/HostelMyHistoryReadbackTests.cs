using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class HostelMyHistoryReadbackTests
{
    [Fact]
    public async Task Print_reuses_fresh_authorized_self_history_query()
    {
        var service = new Mock<IHostelService>();
        var page = new PagedResult<HostelAllocationHistoryRowDto>
        {
            Page = 2, PageSize = 20, TotalCount = 21,
            Items = new List<HostelAllocationHistoryRowDto>()
        };
        service.Setup(x => x.GetMyAllocationHistoryAsync(2, 20, HostelAllocationState.Closed,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>.SuccessResponse(page));
        var controller = new HostelOperationsController(service.Object);
        var result = await controller.PrintMyHistory(2, HostelAllocationState.Closed);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("AllocationHistoryPrint", view.ViewName);
        Assert.Same(page, view.Model);
        service.Verify(x => x.GetMyAllocationHistoryAsync(2, 20, HostelAllocationState.Closed,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Print_propagates_self_history_denial()
    {
        var service = new Mock<IHostelService>();
        service.Setup(x => x.GetMyAllocationHistoryAsync(1, 20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>.ErrorResponse(
                "Hostel permission is required.", 403));
        var result = await new HostelOperationsController(service.Object).PrintMyHistory();
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
