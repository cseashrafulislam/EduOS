using System.Globalization;
using System.Text;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class FacilityWorkOrdersCsvExportTests
{
    [Theory]
    [InlineData("en-BD", "Request,Campus,Space", "VerifiedClosedUtc")]
    [InlineData("bn-BD", "অনুরোধ,ক্যাম্পাস,স্থাপনা", "যাচাইকৃত বন্ধের সময় (UTC)")]
    public async Task Export_uses_current_locale_and_preserves_formula_safe_canonical_values(
        string culture, string heading, string verificationHeading)
    {
        var service = new Mock<IFacilityWorkflowService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Ok(1, 1, new FacilityRequestDto
            {
                Id = 17, CampusId = 9, FacilitySpaceId = 3,
                CategoryCode = "ELECTRICAL", PriorityCode = "High",
                Summary = "=SUM(1,1)", StateCode = "Approved",
                WorkOrderState = "Closed", SlaBreached = true,
                CompletedAtUtc = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc),
                VerifiedAtUtc = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)
            }));
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var file = Assert.IsType<FileContentResult>(
                await new FacilityWorkOrdersApiController(service.Object)
                    .Export(null, null, false, CancellationToken.None));
            var csv = Encoding.UTF8.GetString(file.FileContents);
            Assert.StartsWith("\uFEFF" + heading, csv);
            Assert.Contains(verificationHeading, csv.Split("\r\n")[0]);
            Assert.Contains("\"True\"", csv);
            Assert.Contains("2026", csv);
            Assert.Contains("\"'=SUM(1,1)\"", csv);
            Assert.Contains("\"ELECTRICAL\"", csv);
            Assert.Contains("\"Closed\"", csv);
            Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
        service.Verify(x => x.ListAsync(null, null, 1, 100,
            It.IsAny<CancellationToken>(), false), Times.Once);
    }

    [Fact]
    public async Task Export_rejects_duplicate_ids_or_changed_total_across_authorized_pages()
    {
        foreach (var drift in new[] { "duplicate", "count" })
        {
            var service = new Mock<IFacilityWorkflowService>();
            service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>(), false))
                .ReturnsAsync(Ok(1, 101, Enumerable.Range(1, 100)
                    .Select(i => new FacilityRequestDto { Id = i }).ToArray()));
            service.Setup(x => x.ListAsync(null, null, 2, 100, It.IsAny<CancellationToken>(), false))
                .ReturnsAsync(Ok(2, drift == "count" ? 102 : 101,
                    new FacilityRequestDto { Id = drift == "duplicate" ? 100 : 101 }));
            var result = Assert.IsType<ObjectResult>(
                await new FacilityWorkOrdersApiController(service.Object)
                    .Export(null, null, false, CancellationToken.None));
            Assert.Equal(409, result.StatusCode);
        }
    }

    [Fact]
    public async Task Export_keeps_hard_limit_and_propagates_service_denial()
    {
        var service = new Mock<IFacilityWorkflowService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Ok(1, 501));
        var controller = new FacilityWorkOrdersApiController(service.Object);
        Assert.Equal(413, Assert.IsType<ObjectResult>(
            await controller.Export(null, null, false, CancellationToken.None)).StatusCode);

        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(ApiResponse<PagedResult<FacilityRequestDto>>
                .ErrorResponse("Facilities access denied.", 403));
        Assert.Equal(403, Assert.IsType<ObjectResult>(
            await controller.Export(null, null, false, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task Export_emits_all_rows_when_two_pages_are_stable()
    {
        var service = new Mock<IFacilityWorkflowService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Ok(1, 101, Enumerable.Range(1, 100)
                .Select(i => new FacilityRequestDto { Id = i }).ToArray()));
        service.Setup(x => x.ListAsync(null, null, 2, 100, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Ok(2, 101, new FacilityRequestDto { Id = 101 }));
        var file = Assert.IsType<FileContentResult>(
            await new FacilityWorkOrdersApiController(service.Object)
                .Export(null, null, false, CancellationToken.None));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("\"101\"", csv);
        Assert.Equal(102, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static ApiResponse<PagedResult<FacilityRequestDto>> Ok(
        int page, int total, params FacilityRequestDto[] rows) =>
        ApiResponse<PagedResult<FacilityRequestDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = 100, TotalCount = total, Items = rows.ToList()
        });
}
