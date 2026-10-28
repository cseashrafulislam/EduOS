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

public sealed class StudentAttendanceRosterExportTests
{
    [Fact]
    public void Print_and_csv_are_manager_only_module_gated_rate_limited_and_no_store()
    {
        var controller = typeof(AttendanceController);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "ATTENDANCE",
            Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true)
                .Cast<RequireModuleAttribute>()).Policy);
        foreach (var name in new[] {
            nameof(AttendanceController.ExportFinalizedSessionCsv),
            nameof(AttendanceController.PrintFinalizedSession) })
        {
            var action = controller.GetMethod(name)!;
            Assert.Equal("TenantAdmin,Principal,VicePrincipal",
                Assert.Single(action.GetCustomAttributes(typeof(AuthorizeAttribute), false)
                    .Cast<AuthorizeAttribute>()).Roles);
            Assert.True(Assert.Single(action.GetCustomAttributes(typeof(ResponseCacheAttribute), false)
                .Cast<ResponseCacheAttribute>()).NoStore);
            Assert.Equal("ApiPolicy", Assert.Single(
                action.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false)
                    .Cast<EnableRateLimitingAttribute>()).PolicyName);
            Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute), false));
            Assert.Empty(action.GetCustomAttributes(typeof(HttpPostAttribute), false));
        }
    }

    [Fact]
    public async Task Csv_exports_only_requested_authorized_page_and_neutralizes_spreadsheet_formulas()
    {
        var service = new Mock<IStudentAttendanceReportService>();
        var roster = new StudentAttendanceRosterDto {
            AttendanceSessionId = 40, AcademicBatchId = 10,
            AttendanceDate = new DateOnly(2026, 10, 5), Page = 2,
            PageSize = 50, TotalPages = 3, IsFinalized = true, IsHistoricalReadOnly = true,
            Students = new[] { new StudentAttendanceDto {
                RollNo = "-42", StudentCode = "S-1",
                StudentName = "=HYPERLINK(\"https://invalid.example\",\"click\")",
                State = AttendanceState.Absent, Remarks = " +SUM(1,1)"
            } }
        };
        service.Setup(x => x.GetFinalizedSessionRosterAsync(40, 2, 50,
            It.IsAny<CancellationToken>())).ReturnsAsync(
                ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(roster));
        var controller = new AttendanceController(service.Object);

        var result = Assert.IsType<FileContentResult>(
            await controller.ExportFinalizedSessionCsv(40, 2));
        Assert.Equal("attendance-session-40-page-2.csv", result.FileDownloadName);
        Assert.StartsWith("text/csv", result.ContentType);
        var csv = Encoding.UTF8.GetString(result.FileContents);
        Assert.StartsWith("\uFEFFSession ID,Date,Batch ID,Page,", csv);
        Assert.Contains("\"'-42\"", csv);
        Assert.Contains("'=HYPERLINK(\"\"https://invalid.example\"\"", csv);
        Assert.Contains("\"' +SUM(1,1)\"", csv);
        Assert.Contains(",\"Absent\",", csv);
        Assert.DoesNotContain("page-1", csv);
        service.VerifyAll();
    }

    [Fact]
    public async Task Print_uses_same_scoped_page_and_errors_fail_closed()
    {
        var service = new Mock<IStudentAttendanceReportService>();
        var roster = new StudentAttendanceRosterDto {
            AttendanceSessionId = 40, AcademicBatchId = 10, Page = 3,
            IsFinalized = true, IsHistoricalReadOnly = true
        };
        service.Setup(x => x.GetFinalizedSessionRosterAsync(40, 3, 50,
            It.IsAny<CancellationToken>())).ReturnsAsync(
                ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(roster));
        service.Setup(x => x.GetFinalizedSessionRosterAsync(999, 1, 50,
            It.IsAny<CancellationToken>())).ReturnsAsync(
                ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Not found.", 404));
        service.Setup(x => x.GetFinalizedSessionRosterAsync(40, 0, 50,
            It.IsAny<CancellationToken>())).ReturnsAsync(
                ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Invalid page.", 400));
        var controller = new AttendanceController(service.Object);
        var print = Assert.IsType<ViewResult>(await controller.PrintFinalizedSession(40, 3));
        Assert.Equal("SessionPrint", print.ViewName);
        Assert.Same(roster, print.Model);
        Assert.Equal(404, Assert.IsType<ObjectResult>(
            await controller.ExportFinalizedSessionCsv(999)).StatusCode);
        Assert.Equal(400, Assert.IsType<ObjectResult>(
            await controller.PrintFinalizedSession(40, 0)).StatusCode);
        service.VerifyAll();
    }
}
