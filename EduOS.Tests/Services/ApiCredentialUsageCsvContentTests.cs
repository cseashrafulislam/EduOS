using System.Text;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ApiCredentialUsageCsvContentTests
{
    [Fact]
    public async Task Export_returns_bounded_csv_without_unmetered_fields()
    {
        var page = new PagedResult<UsageStatisticsDto>
        {
            Page = 1, PageSize = 100, TotalCount = 250,
            Items = new List<UsageStatisticsDto>
            {
                new()
                {
                    PeriodStart = new DateOnly(2026, 10, 18),
                    PeriodEnd = new DateOnly(2026, 10, 18),
                    ActiveStudents = 10, ActiveEmployees = 2, ActiveCampuses = 1,
                    StorageBytes = 987654321, SmsCount = 87654321, EmailCount = 7654321
                }
            }
        };
        var service = new Mock<IApiCredentialUsageService>(MockBehavior.Strict);
        service.Setup(x => x.GetUsageAsync(It.IsAny<string>(), 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<UsageStatisticsDto>>.SuccessResponse(page));
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "example";
        var controller = new ApiCredentialUsageApiController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        var file = Assert.IsType<FileContentResult>(await controller.Export());
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Contains("2026-10-18,2026-10-18,10,2,1", csv);
        Assert.DoesNotContain("StorageBytes", csv);
        Assert.DoesNotContain("987654321", csv);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        service.Verify(x => x.GetUsageAsync(It.IsAny<string>(), 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }
}
