using System.Globalization;
using System.Security.Claims;
using EduOS.App.Security;
using Microsoft.AspNetCore.Http;
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
    public async Task Export_uses_one_authorized_snapshot_and_escapes_spreadsheet_formulas(
        string culture, string heading, string lastHeading)
    {
        var service = new Mock<IFacilityWorkflowService>();
        service.Setup(x => x.ExportSnapshotAsync(null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(new FacilityRequestDto
            {
                Id = 17, CampusId = 9, FacilitySpaceId = 3,
                CategoryCode = "ELECTRICAL", PriorityCode = "High",
                Summary = " \t=SUM(1,1)", StateCode = "Approved",
                WorkOrderState = "Closed", SlaBreached = true,
                CompletedAtUtc = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc),
                VerifiedAtUtc = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)
            }));
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var file = Assert.IsType<FileContentResult>(
                await Controller(service.Object)
                    .Export(null, null, false, CancellationToken.None));
            var csv = Encoding.UTF8.GetString(file.FileContents);
            Assert.StartsWith("\uFEFF" + heading, csv);
            Assert.Contains(lastHeading, csv.Split("\r\n")[0]);
            Assert.Contains("\"' \t=SUM(1,1)\"", csv);
            Assert.Contains("\"ELECTRICAL\"", csv);
            Assert.Contains("\"Closed\"", csv);
            Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        }
        finally { CultureInfo.CurrentUICulture = original; }
        service.Verify(x => x.ExportSnapshotAsync(null, null, false,
            It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.ListAsync(It.IsAny<long?>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(413)]
    public async Task Export_propagates_snapshot_denial_or_conflict_without_partial_csv(int code)
    {
        var service = new Mock<IFacilityWorkflowService>();
        service.Setup(x => x.ExportSnapshotAsync(3, "Approved", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<FacilityRequestDto>>
                .ErrorResponse("Export rejected.", code));
        var result = Assert.IsType<ObjectResult>(await Controller(service.Object)
            .Export(3, "Approved", true, CancellationToken.None));
        Assert.Equal(code, result.StatusCode);
    }

    [Fact]
    public async Task Export_writes_every_authorized_row_from_a_single_snapshot()
    {
        var service = new Mock<IFacilityWorkflowService>();
        service.Setup(x => x.ExportSnapshotAsync(null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Enumerable.Range(1, 101).Select(i =>
                new FacilityRequestDto { Id = i, Summary = "Safe" }).ToArray()));
        var file = Assert.IsType<FileContentResult>(await new FacilityWorkOrdersApiController(service.Object)
            .Export(null, null, false, CancellationToken.None));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("\"101\"", csv);
        Assert.Equal(102, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Export_without_authenticated_session_does_not_read_private_snapshot()
    {
        var service = new Mock<IFacilityWorkflowService>();
        var controller = new FacilityWorkOrdersApiController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var denied = await controller.Export(null, null, false, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(denied);
        service.Verify(x => x.ExportSnapshotAsync(It.IsAny<long?>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static FacilityWorkOrdersApiController Controller(IFacilityWorkflowService service)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("TenantId", "12"),
             new Claim(AuthSessionRegistry.ClaimType, new string('A', 48))], "Test"));
        return new FacilityWorkOrdersApiController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static ApiResponse<IReadOnlyList<FacilityRequestDto>> Ok(params FacilityRequestDto[] rows) =>
        ApiResponse<IReadOnlyList<FacilityRequestDto>>.SuccessResponse(rows);
}
