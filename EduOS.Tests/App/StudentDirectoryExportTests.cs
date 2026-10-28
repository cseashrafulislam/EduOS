using System.Globalization;
using System.Text;
using EduOS.App.Controllers.Api;
using EduOS.App.Reporting;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class StudentDirectoryExportTests
{
    [Fact]
    public void Csv_is_utf8_formula_safe_and_excludes_private_contact_and_person_data()
    {
        var rows = new[] { new StudentDirectoryListItemDto
        {
            StudentCode = "=HYPERLINK(\"https://example.test\")",
            FullName = "  +SUM(1,2)",
            RollNo = "@external",
            AcademicYear = "2026",
            AcademicLevelName = "Class, Six",
            AcademicBatchName = "A\"B",
            Status = "Active",
            MaskedMobile = "••••5678"
        }};
        var text = Encoding.UTF8.GetString(StudentDirectoryCsvWriter.Write(rows));
        Assert.StartsWith("\uFEFFStudent Code,Full Name,Roll", text);
        Assert.Contains("\"'=HYPERLINK(\"\"https://example.test\"\")\"", text);
        Assert.Contains("\"'  +SUM(1,2)\"", text);
        Assert.Contains("\"'@external\"", text);
        Assert.Contains("\"Class, Six\"", text);
        Assert.Contains("\"A\"\"B\"", text);
        Assert.DoesNotContain("5678", text);
        Assert.DoesNotContain("Guardian", text);
        var bangla = Encoding.UTF8.GetString(StudentDirectoryCsvWriter.Write(rows, new CultureInfo("bn-BD")));
        Assert.StartsWith("\uFEFFশিক্ষার্থী কোড,পূর্ণ নাম,রোল", bangla);
        Assert.Contains("\"'=HYPERLINK", bangla);
        Assert.Throws<ArgumentOutOfRangeException>(() => StudentDirectoryCsvWriter.Write(
            Enumerable.Range(0, 101).Select(_ => new StudentDirectoryListItemDto()).ToArray()));
    }

    [Fact]
    public async Task Export_uses_authorized_service_page_and_propagates_denial_without_a_file()
    {
        var service = new Mock<IStudentDirectoryService>();
        var query = new StudentDirectoryQueryDto { Page = 2, PageSize = 20, Search = "STU" };
        service.Setup(x => x.GetPageAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<StudentDirectoryListItemDto>>.SuccessResponse(
                new PagedResult<StudentDirectoryListItemDto>
                {
                    Page = 2, PageSize = 20, TotalCount = 21,
                    Items = new List<StudentDirectoryListItemDto>
                    {
                        new() { StudentCode = "STU-21", FullName = "Learner", Status = "Active" }
                    }
                }));
        var controller = new StudentsController(service.Object);
        var result = await controller.ExportPage(query, CancellationToken.None);
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("students-page-2.csv", file.FileDownloadName);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Contains("STU-21", Encoding.UTF8.GetString(file.FileContents));
        service.Verify(x => x.GetPageAsync(query, It.IsAny<CancellationToken>()), Times.Once);

        service.Setup(x => x.GetPageAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<StudentDirectoryListItemDto>>.ErrorResponse(
                "Student directory access denied.", 403));
        var denied = Assert.IsType<ObjectResult>(await controller.ExportPage(query, CancellationToken.None));
        Assert.Equal(403, denied.StatusCode);
    }

    [Fact]
    public void Export_route_preserves_existing_role_module_and_no_store_guards()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.App", "Controllers", "Api", "StudentsController.cs"));
        Assert.Contains("[Authorize(Roles = \"TenantAdmin,AdmissionOfficer\")]", source);
        Assert.Contains("[RequireModule(\"STUDENT\")]", source);
        Assert.Contains("[ResponseCache(NoStore = true", source);
        Assert.Contains("[HttpGet(\"export-page\")]", source);
        Assert.DoesNotContain("IgnoreQueryFilters", source);
        var js = File.ReadAllText(FindRepositoryFile("EduOS.App", "wwwroot", "js", "students", "index.js"));
        Assert.Contains("credentials: 'same-origin'", js);
        Assert.Contains("cache: 'no-store'", js);
        Assert.Contains("directoryParams()", js);
        Assert.DoesNotContain("innerHTML", js);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
