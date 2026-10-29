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
            nameof(AttendanceController.PrintFinalizedSession),
            nameof(AttendanceController.ExportSessionRegisterCsv),
            nameof(AttendanceController.PrintSessionRegister) })
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
        service.Setup(x => x.GetFilteredFinalizedSessionRosterAsync(40, null, null, 2, 50,
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
        service.Setup(x => x.GetFilteredFinalizedSessionRosterAsync(40, null, null, 3, 50,
            It.IsAny<CancellationToken>())).ReturnsAsync(
                ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(roster));
        service.Setup(x => x.GetFilteredFinalizedSessionRosterAsync(999, null, null, 1, 50,
            It.IsAny<CancellationToken>())).ReturnsAsync(
                ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Not found.", 404));
        service.Setup(x => x.GetFilteredFinalizedSessionRosterAsync(40, null, null, 0, 50,
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
    [Fact]
    public async Task Session_register_csv_reauthorizes_scoped_page_and_escapes_formulas()
    {
        var service = new Mock<IStudentAttendanceReportService>();
        var query = new StudentAttendanceReportQueryDto {
            AcademicBatchId = 10, FromDate = new DateOnly(2026, 10, 1),
            ToDate = new DateOnly(2026, 10, 10), Page = 2, PageSize = 100
        };
        var page = new PagedResult<StudentAttendanceSessionRegisterDto> {
            Page = 2, PageSize = 20, TotalCount = 22,
            Items = new List<StudentAttendanceSessionRegisterDto> { new StudentAttendanceSessionRegisterDto {
                SessionId = 42, AttendanceDate = new DateOnly(2026, 10, 5),
                SubjectOfferingCode = "=HYPERLINK(1)", Marked = 2, Absent = 1
            } }
        };
        service.Setup(x => x.GetSessionRegisterAsync(
            It.Is<StudentAttendanceReportQueryDto>(q => q.AcademicBatchId == 10 &&
                q.Page == 2 && q.PageSize == 20), "empty-draft",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<StudentAttendanceSessionRegisterDto>>.SuccessResponse(page));
        var controller = new AttendanceController(service.Object);
        var result = Assert.IsType<FileContentResult>(
            await controller.ExportSessionRegisterCsv(query, "empty-draft"));
        Assert.Equal("attendance-session-register-10-page-2.csv", result.FileDownloadName);
        var csv = Encoding.UTF8.GetString(result.FileContents);
        Assert.Contains("\"'=HYPERLINK(1)\"", csv);
        Assert.Contains("\"Draft\"", csv);
        service.VerifyAll();
    }

    [Fact]
    public async Task Session_register_print_uses_authorized_page_and_denied_exports_fail_closed()
    {
        var service = new Mock<IStudentAttendanceReportService>();
        var query = new StudentAttendanceReportQueryDto {
            AcademicBatchId = 10, FromDate = new DateOnly(2026, 10, 1),
            ToDate = new DateOnly(2026, 10, 10), Page = 1, PageSize = 100
        };
        var page = new PagedResult<StudentAttendanceSessionRegisterDto> {
            Page = 1, PageSize = 20, TotalCount = 1,
            Items = new List<StudentAttendanceSessionRegisterDto> { new StudentAttendanceSessionRegisterDto { SessionId = 9 } }
        };
        service.Setup(x => x.GetSessionRegisterAsync(
            It.Is<StudentAttendanceReportQueryDto>(q => q.PageSize == 20 && q.AcademicBatchId == 10),
            "finalized", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<StudentAttendanceSessionRegisterDto>>.SuccessResponse(page));
        var controller = new AttendanceController(service.Object);
        var result = Assert.IsType<ViewResult>(
            await controller.PrintSessionRegister(query, "finalized"));
        Assert.Equal("SessionRegisterPrint", result.ViewName);
        Assert.Same(page, result.Model);
        Assert.Equal("finalized", controller.ViewData["StatusFilter"]);
        service.VerifyAll();

        service = new Mock<IStudentAttendanceReportService>();
        service.Setup(x => x.GetSessionRegisterAsync(
            It.IsAny<StudentAttendanceReportQueryDto>(), "finalized",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<StudentAttendanceSessionRegisterDto>>.ErrorResponse(
                "Academic batch not found.", 404));
        controller = new AttendanceController(service.Object);
        Assert.Equal(404, Assert.IsType<ObjectResult>(
            await controller.ExportSessionRegisterCsv(query, "finalized")).StatusCode);
        Assert.Equal(404, Assert.IsType<ObjectResult>(
            await controller.PrintSessionRegister(query, "finalized")).StatusCode);
        service.VerifyAll();
    }

}
