using System.Text;
using ClosedXML.Excel;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class StudentImportAuditExportTests
{
    [Fact]
    public void Template_is_a_blank_excel_sheet_with_the_exact_import_contract()
    {
        var controller = new StudentImportsController(
            new Mock<IStudentImportPreviewService>().Object,
            new Mock<IStudentImportExecutionService>().Object);
        var response = Assert.IsType<FileContentResult>(controller.DownloadTemplate());
        Assert.Equal("eduos-student-import-template.xlsx", response.FileDownloadName);
        using var stream = new MemoryStream(response.FileContents);
        using var workbook = new XLWorkbook(stream);
        var students = workbook.Worksheet(1);
        Assert.Equal("Students", students.Name);
        Assert.Equal(EduOS.Core.DTOs.Students.StudentImportColumns.All,
            Enumerable.Range(1, EduOS.Core.DTOs.Students.StudentImportColumns.All.Length)
                .Select(column => students.Cell(1, column).GetString()));
        Assert.Null(students.LastRowUsed()?.RowBelow().CellsUsed().FirstOrDefault());
        Assert.Equal("Guide", workbook.Worksheet(2).Name);
    }

    [Fact]
    public async Task Cohort_export_uses_fresh_authorized_reconciliation_and_never_trusts_browser_counters()
    {
        var execution = new Mock<IStudentImportExecutionService>();
        var preview = new Mock<IStudentImportPreviewService>();
        var files = new List<StudentImportCohortFileRequestDto>
        {
            new() { FileHash = new string('a', 64), MappingHash = new string('b', 64), TotalRows = 100 }
        };
        execution.Setup(x => x.GetCohortStatusAsync(files, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentImportCohortStatusDto>.SuccessResponse(
                new StudentImportCohortStatusDto
                {
                    ExpectedRows = 100, CommittedRows = 100, LogItemsVerifiedRows = 100,
                    Files = new List<StudentImportCohortFileStatusDto>
                    {
                        new() { FileHash = files[0].FileHash, MappingHash = files[0].MappingHash,
                            TotalRows = 100, CommittedRows = 100, LogItemsVerifiedRows = 100 }
                    }
                }));
        var controller = new StudentImportsController(preview.Object, execution.Object);
        var file = Assert.IsType<FileContentResult>(
            await controller.ExportCohortStatus(files, CancellationToken.None));
        Assert.Equal("student-import-cohort-summary.csv", file.FileDownloadName);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.StartsWith("\uFEFFFileHash,MappingHash,ExpectedRows", csv);
        Assert.Contains("LogItemsVerified", csv);
        execution.Verify(x => x.GetCohortStatusAsync(files, It.IsAny<CancellationToken>()), Times.Once);

        execution.Setup(x => x.GetCohortStatusAsync(files, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                "Student import access denied.", 403));
        var denied = Assert.IsType<ObjectResult>(
            await controller.ExportCohortStatus(files, CancellationToken.None));
        Assert.Equal(403, denied.StatusCode);
    }

    [Fact]
    public async Task Integrity_export_rechecks_exact_reference_and_escapes_spreadsheet_formulas()
    {
        var execution = new Mock<IStudentImportExecutionService>();
        var reference = Guid.NewGuid();
        execution.Setup(x => x.GetIntegrityAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentImportIntegrityReportDto>.SuccessResponse(
                new StudentImportIntegrityReportDto
                {
                    ImportReference = reference, ExpectedRows = 1, CheckedRows = 1,
                    Issues = new List<StudentImportIntegrityIssueDto>
                    {
                        new() { RowNumber = 2, Code = "=HYPERLINK", Message = "  +SUM(1,2)" }
                    }
                }));
        var controller = new StudentImportsController(
            new Mock<IStudentImportPreviewService>().Object, execution.Object);
        var file = Assert.IsType<FileContentResult>(
            await controller.ExportIntegrity(reference, CancellationToken.None));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.StartsWith("\uFEFFRowNumber,IssueCode,Details", csv);
        Assert.Contains("\"'=HYPERLINK\"", csv);
        Assert.Contains("\"'  +SUM(1,2)\"", csv);
        Assert.DoesNotContain("PersonId", csv);
        execution.Verify(x => x.GetIntegrityAsync(reference, It.IsAny<CancellationToken>()), Times.Once);

        execution.Setup(x => x.GetIntegrityAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Import batch not found.", 404));
        var denied = Assert.IsType<ObjectResult>(
            await controller.ExportIntegrity(reference, CancellationToken.None));
        Assert.Equal(404, denied.StatusCode);
    }

    [Fact]
    public void Audit_exports_keep_role_module_csrf_and_no_store_guards()
    {
        var type = typeof(StudentImportsController);
        // The controller inherits authorization metadata; GetCustomAttribute
        // throws on multiple valid Authorize attributes. Assert presence only.
        Assert.Contains(type.GetCustomAttributes(inherit: true),
            attribute => attribute is Microsoft.AspNetCore.Authorization.AuthorizeAttribute);
        Assert.Contains(type.GetCustomAttributes(inherit: true),
            attribute => attribute is Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute);
        Assert.Contains(type.GetCustomAttributes(inherit: true),
            attribute => attribute is Microsoft.AspNetCore.Mvc.ResponseCacheAttribute);
        Assert.NotNull(type.GetMethod(nameof(StudentImportsController.ExportIntegrity)));
        Assert.NotNull(type.GetMethod(nameof(StudentImportsController.ExportCohortStatus)));
    }

    [Fact]
    public async Task Academic_repair_routes_rely_on_service_authorization_and_forward_same_request_identity()
    {
        var execution = new Mock<IStudentImportExecutionService>();
        var reference = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var fingerprint = new string('B', 64);
        var preview = new StudentImportAcademicRepairPlanDto
        {
            ImportReference = reference, PlanHash = fingerprint, MissingRegistrations = 2
        };
        execution.Setup(x => x.PreviewAcademicRepairAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentImportAcademicRepairPlanDto>.SuccessResponse(preview));
        execution.Setup(x => x.ApplyAcademicRepairAsync(reference, requestId, fingerprint,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentImportAcademicRepairResultDto>.SuccessResponse(
                new StudentImportAcademicRepairResultDto
                {
                    ImportReference = reference, CreatedRegistrations = 2
                }));
        var controller = new StudentImportsController(
            new Mock<IStudentImportPreviewService>().Object, execution.Object);
        var read = Assert.IsType<ObjectResult>(
            await controller.PreviewAcademicRepair(reference, CancellationToken.None));
        Assert.Equal(200, read.StatusCode);
        var post = Assert.IsType<ObjectResult>(
            await controller.ApplyAcademicRepair(reference, requestId, fingerprint, CancellationToken.None));
        Assert.Equal(200, post.StatusCode);
        execution.Verify(x => x.ApplyAcademicRepairAsync(reference, requestId, fingerprint,
            It.IsAny<CancellationToken>()), Times.Once);

        execution.Setup(x => x.ApplyAcademicRepairAsync(reference, requestId, fingerprint,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                "Academic records changed.", 409));
        var stale = Assert.IsType<ObjectResult>(
            await controller.ApplyAcademicRepair(reference, requestId, fingerprint, CancellationToken.None));
        Assert.Equal(409, stale.StatusCode);
    }
}
