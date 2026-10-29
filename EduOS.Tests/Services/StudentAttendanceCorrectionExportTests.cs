using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentAttendanceCorrectionExportTests
{
    [Fact]
    public void Export_and_print_require_manager_module_rate_limit_and_no_store()
    {
        var type = typeof(AttendanceCorrectionExportsController);
        Assert.Equal("TenantAdmin,Principal,VicePrincipal",
            Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>()).Roles);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "ATTENDANCE",
            Assert.Single(type.GetCustomAttributes(typeof(RequireModuleAttribute), true)
                .Cast<RequireModuleAttribute>()).Policy);
        Assert.True(Assert.Single(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>()).NoStore);
        Assert.Equal("ApiPolicy", Assert.Single(type.GetCustomAttributes(
            typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>()).PolicyName);
        foreach (var action in new[] { nameof(AttendanceCorrectionExportsController.ExportCsv),
            nameof(AttendanceCorrectionExportsController.Print) })
        {
            Assert.Single(type.GetMethod(action)!.GetCustomAttributes(typeof(HttpGetAttribute), false));
            Assert.Empty(type.GetMethod(action)!.GetCustomAttributes(typeof(HttpPostAttribute), false));
        }
    }

    [Fact]
    public async Task Csv_uses_fresh_scoped_page_and_neutralizes_formula_values()
    {
        var query = Query();
        var service = new Mock<IStudentAttendanceService>();
        var data = new PagedResult<StudentAttendanceCorrectionReportRowDto>
        {
            Page = 2, PageSize = 20, TotalCount = 25,
            Items = new List<StudentAttendanceCorrectionReportRowDto>
            {
                new() {
                    AttendanceDate = new DateOnly(2026, 10, 15),
                    RollNo = "-42", StudentCode = "ST-42",
                    StudentName = "=HYPERLINK(\"https://invalid.example\",\"click\")",
                    SubjectOfferingId = 91,
                    PreviousState = AttendanceState.Absent, NewState = AttendanceState.Present,
                    Reason = " +SUM(1,1)", ChangedByUserId = 7,
                    ChangedAt = new DateTime(2026, 10, 16, 10, 30, 0)
                }
            }
        };
        service.Setup(x => x.GetCorrectionReportAsync(query, 2, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<StudentAttendanceCorrectionReportRowDto>>
                .SuccessResponse(data));
        var controller = new AttendanceCorrectionExportsController(service.Object);
        var result = Assert.IsType<FileContentResult>(await controller.ExportCsv(query, 2));
        Assert.Equal("attendance-corrections-batch-10-page-2.csv", result.FileDownloadName);
        Assert.StartsWith("text/csv", result.ContentType);
        var csv = Encoding.UTF8.GetString(result.FileContents);
        Assert.StartsWith("\uFEFFAttendance date,Roll,", csv);
        Assert.Contains("\"'-42\"", csv);
        Assert.Contains("'=HYPERLINK(\"\"https://invalid.example\"\"", csv);
        Assert.Contains("\"' +SUM(1,1)\"", csv);
        Assert.Contains(",\"Absent\",\"Present\",", csv);
        service.VerifyAll();
    }

    [Fact]
    public async Task Print_and_denied_exports_use_the_same_authorized_service()
    {
        var query = Query();
        var service = new Mock<IStudentAttendanceService>();
        var data = new PagedResult<StudentAttendanceCorrectionReportRowDto>
        {
            Page = 3, PageSize = 20, TotalCount = 41
        };
        service.Setup(x => x.GetCorrectionReportAsync(query, 3, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<StudentAttendanceCorrectionReportRowDto>>
                .SuccessResponse(data));
        service.Setup(x => x.GetCorrectionReportAsync(query, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<StudentAttendanceCorrectionReportRowDto>>
                .ErrorResponse("Academic batch not found.", 404));
        var controller = new AttendanceCorrectionExportsController(service.Object);
        var view = Assert.IsType<ViewResult>(await controller.Print(query, 3));
        Assert.Equal("~/Views/Attendance/CorrectionAuditPrint.cshtml", view.ViewName);
        Assert.Same(data, view.Model);
        Assert.Equal("10", controller.ViewData["BatchId"]?.ToString());
        Assert.Equal(404, Assert.IsType<ObjectResult>(
            await controller.ExportCsv(query)).StatusCode);
        Assert.Equal(404, Assert.IsType<ObjectResult>(
            await controller.Print(query)).StatusCode);
        service.VerifyAll();
    }

    private static StudentAttendanceCorrectionReportQueryDto Query() => new()
    {
        AcademicBatchId = 10, FromDate = new DateOnly(2026, 10, 1),
        ToDate = new DateOnly(2026, 10, 20),
        StudentSearch = "ST", PreviousState = AttendanceState.Absent,
        NewState = AttendanceState.Present
    };
}
