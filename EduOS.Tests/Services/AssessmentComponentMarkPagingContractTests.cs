using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Interfaces.IServices;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentComponentMarkPagingContractTests
{
    private static string Read(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }

    [Theory]
    [InlineData(1, 50, true)]
    [InlineData(0, 50, false)]
    [InlineData(1, 101, false)]
    [InlineData(100001, 25, false)]
    public void Component_roster_has_bounded_page_validation(int page, int size, bool valid)
    {
        var query = new AssessmentComponentMarkRosterQueryDto
        {
            AssessmentId = 1, AcademicBatchId = 1, AssessmentSubjectId = 1,
            Page = page, PageSize = size
        };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(query, new ValidationContext(query), errors, true).Should().Be(valid);
    }

    [Fact]
    public void Component_marks_use_canonical_authorized_sql_page_and_page_only_evidence()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.ComponentMarks.cs");
        source.Should().Contain("SearchMarkRosterAsync(new AssessmentMarkRosterPageQueryDto");
        source.Should().Contain("Page = request.Page, PageSize = request.PageSize, Search = request.Search");
        source.Should().Contain("registrations.Contains(x.StudentSubjectRegistrationId)");
        source.Should().Contain("masterIds.Contains(x.StudentAssessmentMarkId)");
        source.Should().Contain("TotalCount = roster.TotalCount");
        source.Should().Contain("request.PageSize is < 1 or > 100");
        source.Should().Contain("AssessmentState.MarksEntry");
        Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkRosterPaging.cs")
            .Should().Contain("CanEditOfferingAsync(offering.Id, ct)");
        typeof(IAssessmentAdministrationService).GetMethod(
            nameof(IAssessmentAdministrationService.GetComponentMarkRosterAsync)).Should().NotBeNull();
    }

    [Fact]
    public void Component_paging_preserves_authorized_api_and_page_only_export()
    {
        Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs")
            .Should().Contain("[HttpGet(\"component-marks/roster\")]");
        var view = Read("EduOS.App/Views/AssessmentAdministration/ComponentMarks.cshtml");
        view.Should().Contain("id=\"cmSearch\"");
        view.Should().Contain("id=\"cmPrev\"");
        view.Should().Contain("id=\"cmNext\"");
        view.Should().Contain("aria-label=\"Component marks guide\"");
        var js = Read("EduOS.App/wwwroot/js/assessment/component-marks.js");
        js.Should().Contain("pageSize: String(state.pageSize)");
        js.Should().Contain("state.total = roster.totalCount");
        js.Should().Contain("confirmDiscard()");
        js.Should().Contain("component-marks-page-");
    }
}
